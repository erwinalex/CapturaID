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
    private readonly ResolutorImagenesIne _resolutorImagenes;
    private readonly ImageStorageOptions _imageOptions;
    private readonly ILogger<PersonasController> _logger;

    public PersonasController(
        SchIdDbContext db,
        IImageStorageService images,
        ResolutorImagenesIne resolutorImagenes,
        IOptions<ImageStorageOptions> imageOptions,
        ILogger<PersonasController> logger)
    {
        _db = db;
        _images = images;
        _resolutorImagenes = resolutorImagenes;
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

    /// <summary>
    /// Busca por nombre o por CURP con un solo término: quien atiende no tiene
    /// por qué decidir de antemano qué está escribiendo.
    /// </summary>
    [HttpGet("buscar")]
    [Authorize(Roles = RolesApi.Consulta)]
    public async Task<ActionResult<IReadOnlyList<ResultadoBusquedaResponse>>> Buscar(
        [FromQuery] string q,
        CancellationToken ct)
    {
        var termino = (q ?? "").Trim();
        if (termino.Length == 0)
        {
            return BadRequest("Escribe algo que buscar.");
        }

        // El término no se registra en el log: es un nombre o un CURP, o sea un
        // dato personal, y el log no es sitio para eso.
        var resultados = await _db.Personas
            .AsNoTracking()
            .Where(p =>
                (p.Nombre != null && EF.Functions.Like(p.Nombre, "%" + termino + "%")) ||
                (p.CURP != null && EF.Functions.Like(p.CURP, "%" + termino + "%")))
            .OrderBy(p => p.Nombre)
            .Take(MaximoResultados)
            .ToListAsync(ct);

        return Ok(resultados.Select(ResultadoBusquedaResponse.DesdeEntidad).ToList());
    }

    /// <summary>Los datos de una persona por Id, para armar su ficha.</summary>
    [HttpGet("{id:long}")]
    [Authorize(Roles = RolesApi.Consulta)]
    public async Task<ActionResult<PersonaResponse>> Obtener(long id, CancellationToken ct)
    {
        var persona = await _db.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.ID == id, ct);
        if (persona is null)
        {
            return NotFound();
        }

        var respuesta = PersonaResponse.DesdeEntidad(persona);
        respuesta.TieneImagenFrente = _images.Exists(persona.ID, ImageSide.Frente);
        respuesta.TieneImagenReverso = _images.Exists(persona.ID, ImageSide.Reverso);

        return Ok(respuesta);
    }

    /// <summary>
    /// Entrega una cara de la identificación, venga de donde venga.
    ///
    /// Que esto lo sirva el servidor es lo que permite consultar desde cualquier
    /// terminal: los archivos están en el disco del servidor, y sin este
    /// endpoint cada PC necesitaría un recurso compartido de Windows apuntando a
    /// una carpeta llena de imágenes de INE.
    ///
    /// La cabecera X-Origen-Imagen dice si salió del archivo o de la columna;
    /// durante la migración es la forma de ver quién ya está migrado sin entrar
    /// a la base a mano.
    /// </summary>
    [HttpGet("{id:long}/imagen/{lado}")]
    [Authorize(Roles = RolesApi.Consulta)]
    public async Task<IActionResult> Imagen(long id, string lado, CancellationToken ct)
    {
        if (!Enum.TryParse<ImageSide>(lado, ignoreCase: true, out var cara))
        {
            return BadRequest("El lado debe ser 'frente' o 'reverso'.");
        }

        var imagen = await _resolutorImagenes.ObtenerAsync(id, cara, ct);
        if (imagen is null)
        {
            return NotFound();
        }

        Response.Headers["X-Origen-Imagen"] = imagen.Origen.ToString();
        return File(imagen.Contenido, "image/jpeg");
    }

    /// <summary>
    /// Tope de resultados. Con la tabla de varios años de un hotel, buscar
    /// "GARCIA" puede traer miles de renglones; nadie los lee, y traerlos hace
    /// lenta la consulta para todos.
    /// </summary>
    public const int MaximoResultados = 200;

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
