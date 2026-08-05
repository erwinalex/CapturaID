using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchId.Api.Data;
using SchId.Api.Data.Entities;
using SchId.Api.Models;
using SchId.Api.Security;
using SchId.Api.Services;
using SchId.Shared;

namespace SchId.Api.Controllers;

/// <summary>
/// Alcance de este controlador: capturar los datos y las imágenes de la INE, y
/// nada más.
///
/// El registro de estancias (entradas, salidas, asignación de habitación) es del
/// PMS, que trabaja directamente contra la base de datos. Esta API NO escribe en
/// dbo.Estancias — solo la lee el job de retención, para saber desde cuándo
/// contar. Lo que el PMS necesita de aquí es el Id que devuelve el registro,
/// para amarrar a la persona con la estancia que él crea.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PersonasController : ControllerBase
{
    private readonly SchIdDbContext _db;
    private readonly IImageStorageService _images;
    private readonly ImageStorageOptions _imageOptions;
    private readonly ILogger<PersonasController> _logger;

    public PersonasController(
        SchIdDbContext db,
        IImageStorageService images,
        IOptions<ImageStorageOptions> imageOptions,
        ILogger<PersonasController> logger)
    {
        _db = db;
        _images = images;
        _imageOptions = imageOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Registra la captura de una INE. Si el CURP ya existe, actualiza el
    /// registro previo en lugar de duplicarlo.
    ///
    /// La comparación contra lo que ya había se hace aquí, en el servidor, y no
    /// en la app: así el kiosko nunca recibe los datos anteriores del huésped,
    /// solo se entera de si hubo alta, actualización o nada que cambiar. Las
    /// imágenes se reemplazan siempre que vengan, porque son las vigentes.
    /// </summary>
    [HttpPost("registro")]
    [Authorize(Roles = RolesApi.Captura)]
    [RequestSizeLimit(20_000_000)] // ~20 MB, suficiente para las dos fotos
    public async Task<ActionResult<RegistroResponse>> Registrar(
        [FromForm] RegistroIneRequest request,
        IFormFile? imagenFrente,
        IFormFile? imagenReverso,
        CancellationToken ct)
    {
        var curp = PersonaMerge.NormalizarCurp(request.Curp);
        if (curp is null)
        {
            return BadRequest("El CURP es obligatorio.");
        }

        // Se leen y validan las imágenes ANTES de tocar la base de datos: si el
        // kiosko mandó algo que no es un JPEG, es preferible rechazar la
        // petición completa a dejar una persona dada de alta sin sus fotos.
        byte[]? frente;
        byte[]? reverso;
        try
        {
            frente = await LeerImagenAsync(imagenFrente, nameof(imagenFrente), ct);
            reverso = await LeerImagenAsync(imagenReverso, nameof(imagenReverso), ct);
        }
        catch (ImagenInvalidaException ex)
        {
            return BadRequest(ex.Message);
        }

        var persona = await _db.Personas
            .FirstOrDefaultAsync(p => p.CURP != null && p.CURP.Trim() == curp, ct);

        var esNuevo = persona is null;
        IReadOnlyList<string> camposActualizados;

        if (persona is null)
        {
            persona = new Persona { CURP = curp };
            PersonaMerge.Aplicar(persona, request);
            persona.FechaAlta = DelphiDateTime.FromDateTime(DateTime.Now);
            persona.UltimaModificacion = persona.FechaAlta;
            camposActualizados = Array.Empty<string>();

            _db.Personas.Add(persona);

            // Se guarda aquí para que la base asigne el ID: el nombre de archivo
            // de las imágenes se calcula a partir de él.
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            camposActualizados = PersonaMerge.Aplicar(persona, request);

            if (camposActualizados.Count > 0)
            {
                persona.UltimaModificacion = DelphiDateTime.FromDateTime(DateTime.Now);
                await _db.SaveChangesAsync(ct);
            }
        }

        var guardoFrente = await GuardarImagenAsync(persona.ID, ImageSide.Frente, frente, ct);
        var guardoReverso = await GuardarImagenAsync(persona.ID, ImageSide.Reverso, reverso, ct);

        var resultado = esNuevo
            ? ResultadoRegistro.Creado
            : camposActualizados.Count > 0
                ? ResultadoRegistro.Actualizado
                : ResultadoRegistro.SinCambios;

        // Se registra QUÉ campos cambiaron, nunca sus valores: el log del
        // servicio no es lugar para datos personales.
        _logger.LogInformation(
            "Captura de INE {Resultado} - PersonaId {Id}, campos actualizados: {Campos}, token: {Token}.",
            resultado,
            persona.ID,
            camposActualizados.Count == 0 ? "(ninguno)" : string.Join(", ", camposActualizados),
            User.Identity?.Name ?? "(desconocido)");

        return Ok(new RegistroResponse
        {
            Id = persona.ID,
            Resultado = resultado,
            CamposActualizados = camposActualizados,
            ImagenFrenteGuardada = guardoFrente,
            ImagenReversoGuardada = guardoReverso
        });
    }

    /// <summary>
    /// Consulta una persona por CURP. No la usa el kiosko (su token no tiene
    /// este rol); está para soporte y herramientas administrativas.
    /// </summary>
    [HttpGet("curp/{curp}")]
    [Authorize(Roles = RolesApi.Consulta)]
    public async Task<ActionResult<PersonaResponse>> BuscarPorCurp(string curp, CancellationToken ct)
    {
        var curpBuscado = PersonaMerge.NormalizarCurp(curp);
        if (curpBuscado is null)
        {
            return BadRequest("El CURP es obligatorio.");
        }

        var persona = await _db.Personas
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CURP != null && p.CURP.Trim() == curpBuscado, ct);

        if (persona is null)
        {
            return NotFound();
        }

        var respuesta = PersonaResponse.DesdeEntidad(persona);
        respuesta.TieneImagenFrente = _images.Exists(persona.ID, ImageSide.Frente);
        respuesta.TieneImagenReverso = _images.Exists(persona.ID, ImageSide.Reverso);

        return Ok(respuesta);
    }

    private async Task<byte[]?> LeerImagenAsync(IFormFile? archivo, string nombreCampo, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0)
        {
            return null;
        }

        if (archivo.Length > _imageOptions.MaxBytesPorImagen)
        {
            throw new ImagenInvalidaException(
                $"{nombreCampo} pesa {archivo.Length} bytes y el máximo permitido es {_imageOptions.MaxBytesPorImagen}.");
        }

        using var memoria = new MemoryStream();
        await using (var origen = archivo.OpenReadStream())
        {
            await origen.CopyToAsync(memoria, ct);
        }

        var contenido = memoria.ToArray();

        if (!ImagenJpeg.EsJpeg(contenido))
        {
            throw new ImagenInvalidaException($"{nombreCampo} no es un archivo JPEG.");
        }

        return contenido;
    }

    private async Task<bool> GuardarImagenAsync(long personaId, ImageSide lado, byte[]? contenido, CancellationToken ct)
    {
        if (contenido is null)
        {
            return false;
        }

        using var stream = new MemoryStream(contenido);
        await _images.SaveAsync(personaId, lado, stream, ct);
        return true;
    }

    private class ImagenInvalidaException : Exception
    {
        public ImagenInvalidaException(string mensaje) : base(mensaje)
        {
        }
    }
}
