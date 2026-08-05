using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchId.Api.Data;
using SchId.Shared;

namespace SchId.Api.Services;

public class RetentionOptions
{
    /// <summary>Días desde la última actividad de la persona tras los cuales sus imágenes son candidatas a borrado.</summary>
    public int DiasRetencion { get; set; } = 365;

    /// <summary>
    /// Interruptor de seguridad. Mientras esté en false, el job solo REGISTRA en el
    /// log quiénes serían candidatos a borrado, sin borrar nada. Revísalo con
    /// calma (y probablemente con tu asesor legal) antes de ponerlo en true.
    /// </summary>
    public bool HabilitarBorradoAutomatico { get; set; } = false;
}

/// <summary>
/// Job en segundo plano que corre una vez al día: borra de disco las imágenes de
/// INE cuyo periodo de retención ya venció. El criterio vive en RetentionPolicy
/// (ahí está explicado el porqué de tomar la última actividad y no solo la
/// última salida).
///
/// Este job es el ÚNICO lugar de la API que lee dbo.Estancias, y solo lee: quien
/// administra esa tabla es el PMS. Por eso todas las consultas van con
/// AsNoTracking.
///
/// La comparación de fechas se hace en memoria y no en la consulta SQL, porque
/// las fechas están guardadas en formato TDateTime de Delphi (numeric) y esa
/// conversión no se puede traducir a SQL vía LINQ. Ver DelphiDateTime.
/// </summary>
public class RetentionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IImageStorageService _images;
    private readonly RetentionOptions _options;
    private readonly ILogger<RetentionCleanupService> _logger;

    public RetentionCleanupService(
        IServiceScopeFactory scopeFactory,
        IImageStorageService images,
        IOptions<RetentionOptions> options,
        ILogger<RetentionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _images = images;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarRevisionAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el job de retención de imágenes de INE.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // El servicio se está deteniendo; salir del ciclo sin ruido.
            }
        }
    }

    private async Task EjecutarRevisionAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchIdDbContext>();

        var actividadPorPersona = await db.Estancias
            .AsNoTracking()
            .Where(e => e.IdPersona != null)
            .GroupBy(e => e.IdPersona!.Value)
            .Select(g => new
            {
                IdPersona = g.Key,
                MaxFIngreso = g.Max(e => e.FIngreso),
                MaxFSalida = g.Max(e => e.FSalida)
            })
            .ToDictionaryAsync(x => x.IdPersona, ct);

        var personas = await db.Personas
            .AsNoTracking()
            .Select(p => new { p.ID, p.FechaAlta, p.UltimaModificacion })
            .ToListAsync(ct);

        var limite = DateTime.Now.AddDays(-_options.DiasRetencion);
        var candidatos = new List<(long Id, DateTime UltimaActividad)>();

        foreach (var persona in personas)
        {
            actividadPorPersona.TryGetValue(persona.ID, out var estancias);

            var ultimaActividad = RetentionPolicy.CalcularUltimaActividad(
                persona.FechaAlta,
                persona.UltimaModificacion,
                estancias?.MaxFIngreso,
                estancias?.MaxFSalida);

            if (!RetentionPolicy.EsCandidato(ultimaActividad, limite))
            {
                continue;
            }

            // Solo interesan quienes de verdad tienen imágenes en disco; si no,
            // el conteo del log se llenaría de personas históricas sin fotos y
            // no se podría revisar de forma útil.
            if (!_images.Exists(persona.ID, ImageSide.Frente) && !_images.Exists(persona.ID, ImageSide.Reverso))
            {
                continue;
            }

            candidatos.Add((persona.ID, ultimaActividad!.Value));
        }

        _logger.LogInformation(
            "Retención de imágenes INE: {Count} persona(s) con imágenes superan {Dias} días desde su última actividad (borrado automático: {Habilitado}).",
            candidatos.Count, _options.DiasRetencion, _options.HabilitarBorradoAutomatico);

        foreach (var (id, ultimaActividad) in candidatos)
        {
            if (!_options.HabilitarBorradoAutomatico)
            {
                _logger.LogInformation(
                    "Candidato a borrado (simulación) - PersonaId {Id}, última actividad {Fecha:yyyy-MM-dd}.",
                    id, ultimaActividad);
                continue;
            }

            _images.Delete(id, ImageSide.Frente);
            _images.Delete(id, ImageSide.Reverso);

            _logger.LogInformation(
                "Imágenes de INE borradas por vencimiento de retención - PersonaId {Id}, última actividad {Fecha:yyyy-MM-dd}.",
                id, ultimaActividad);
        }
    }
}
