using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchId.Api.Data;
using SchId.Shared;

namespace SchId.Api.Services;

public class RetentionOptions
{
    /// <summary>Días desde la ÚLTIMA salida de la persona tras los cuales sus imágenes son candidatas a borrado.</summary>
    public int DiasRetencion { get; set; } = 365;

    /// <summary>
    /// Interruptor de seguridad. Mientras esté en false, el job solo REGISTRA en el
    /// log quiénes serían candidatos a borrado, sin borrar nada. Revísalo con
    /// calma (y probablemente con tu asesor legal) antes de ponerlo en true.
    /// </summary>
    public bool HabilitarBorradoAutomatico { get; set; } = false;
}

/// <summary>
/// Job en segundo plano que corre una vez al día. Por cada persona, calcula la
/// fecha de su estancia más reciente (MAX(FSalida) en Estancias). Si pasaron más
/// de DiasRetencion días desde esa fecha, las imágenes de su INE dejan de ser
/// necesarias para el propósito legal por el que se guardaron (principio de
/// minimización de datos) y son candidatas a borrado.
///
/// Importante: la comparación de fechas se hace en memoria (no en la consulta
/// SQL) porque las fechas están guardadas en formato TDateTime de Delphi
/// (numeric), no como DATETIME nativo, y esa conversión no se puede traducir a
/// SQL vía LINQ. Ver DelphiDateTime para el porqué.
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

        var ultimaSalidaPorPersona = await db.Estancias
            .Where(e => e.IdPersona != null && e.FSalida != null)
            .GroupBy(e => e.IdPersona!.Value)
            .Select(g => new { IdPersona = g.Key, MaxFSalida = g.Max(e => e.FSalida) })
            .ToListAsync(ct);

        var limite = DateTime.Now.AddDays(-_options.DiasRetencion);

        var candidatos = ultimaSalidaPorPersona
            .Select(x => new { x.IdPersona, UltimaSalida = DelphiDateTime.ToDateTime(x.MaxFSalida) })
            .Where(x => x.UltimaSalida.HasValue && x.UltimaSalida.Value < limite)
            .ToList();

        _logger.LogInformation(
            "Retención de imágenes INE: {Count} persona(s) superan {Dias} días desde su última salida (modo borrado automático: {Habilitado}).",
            candidatos.Count, _options.DiasRetencion, _options.HabilitarBorradoAutomatico);

        foreach (var candidato in candidatos)
        {
            if (!_options.HabilitarBorradoAutomatico)
            {
                _logger.LogInformation(
                    "Candidato a borrado (simulación) - PersonaId {Id}, última salida {Fecha:yyyy-MM-dd}.",
                    candidato.IdPersona, candidato.UltimaSalida);
                continue;
            }

            _images.Delete(candidato.IdPersona, ImageSide.Frente);
            _images.Delete(candidato.IdPersona, ImageSide.Reverso);

            _logger.LogInformation(
                "Imágenes de INE borradas por vencimiento de retención - PersonaId {Id}, última salida {Fecha:yyyy-MM-dd}.",
                candidato.IdPersona, candidato.UltimaSalida);
        }
    }
}
