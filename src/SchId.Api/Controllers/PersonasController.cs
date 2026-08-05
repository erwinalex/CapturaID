using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchId.Api.Data;
using SchId.Api.Data.Entities;
using SchId.Api.Models;
using SchId.Api.Services;
using SchId.Shared;

namespace SchId.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PersonasController : ControllerBase
{
    private readonly SchIdDbContext _db;
    private readonly IImageStorageService _images;
    private readonly ILogger<PersonasController> _logger;

    public PersonasController(SchIdDbContext db, IImageStorageService images, ILogger<PersonasController> logger)
    {
        _db = db;
        _images = images;
        _logger = logger;
    }

    /// <summary>Busca una persona existente por CURP, para no duplicar huéspedes recurrentes.</summary>
    [HttpGet("curp/{curp}")]
    public async Task<ActionResult<PersonaResponse>> BuscarPorCurp(string curp)
    {
        var curpBuscado = curp.Trim();

        var persona = await _db.Personas
            .FirstOrDefaultAsync(p => p.CURP != null && p.CURP.Trim() == curpBuscado);

        if (persona is null)
        {
            return NotFound();
        }

        return Ok(PersonaResponse.DesdeEntidad(persona));
    }

    /// <summary>
    /// Registra (o actualiza, si el CURP ya existe) una persona y crea una nueva
    /// estancia con fecha de ingreso = ahora. Recibe las dos imágenes de la INE
    /// (frente y reverso) como multipart/form-data; se guardan en disco, nunca
    /// en la base de datos.
    /// </summary>
    [HttpPost("registro")]
    [RequestSizeLimit(20_000_000)] // ~20 MB, suficiente para las dos fotos
    public async Task<ActionResult<PersonaResponse>> Registrar(
        [FromForm] RegistroIneRequest request,
        IFormFile? imagenFrente,
        IFormFile? imagenReverso,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Curp))
        {
            return BadRequest("El CURP es obligatorio.");
        }

        var curpNormalizado = request.Curp.Trim();

        var persona = await _db.Personas
            .FirstOrDefaultAsync(p => p.CURP != null && p.CURP.Trim() == curpNormalizado, ct);

        var esNuevo = persona is null;
        persona ??= new Persona();

        persona.Nombre = request.Nombre;
        persona.Direccion = request.Direccion;
        persona.CURP = curpNormalizado;
        persona.Telefono = request.Telefono;
        persona.Nacionalidad = request.Nacionalidad;
        persona.Edad = request.Edad;
        persona.Residencia = request.Residencia;
        persona.UltimaModificacion = DelphiDateTime.FromDateTime(DateTime.Now);

        if (esNuevo)
        {
            persona.FechaAlta = DelphiDateTime.FromDateTime(DateTime.Now);
            _db.Personas.Add(persona);

            // Se guarda ahora para obtener el ID generado por la base de datos
            // antes de calcular la ruta de las imágenes (el nombre de archivo
            // depende del ID).
            await _db.SaveChangesAsync(ct);
        }

        if (imagenFrente is not null && imagenFrente.Length > 0)
        {
            await using var contenido = imagenFrente.OpenReadStream();
            await _images.SaveAsync(persona.ID, ImageSide.Frente, contenido, ct);
        }

        if (imagenReverso is not null && imagenReverso.Length > 0)
        {
            await using var contenido = imagenReverso.OpenReadStream();
            await _images.SaveAsync(persona.ID, ImageSide.Reverso, contenido, ct);
        }

        var estancia = new Estancia
        {
            IdPersona = persona.ID,
            FIngreso = DelphiDateTime.FromDateTime(DateTime.Now),
            Hospedado = 1
        };
        _db.Estancias.Add(estancia);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Registro de INE {Accion} - PersonaId {Id}.", esNuevo ? "nuevo" : "actualizado", persona.ID);

        return Ok(PersonaResponse.DesdeEntidad(persona));
    }

    /// <summary>Marca la salida (checkout) de la estancia abierta más reciente de la persona.</summary>
    [HttpPost("{id:long}/checkout")]
    public async Task<IActionResult> Checkout(long id, CancellationToken ct)
    {
        var estancia = await _db.Estancias
            .Where(e => e.IdPersona == id && e.FSalida == null)
            .OrderByDescending(e => e.IdEstancia)
            .FirstOrDefaultAsync(ct);

        if (estancia is null)
        {
            return NotFound("No hay una estancia abierta para esta persona.");
        }

        estancia.FSalida = DelphiDateTime.FromDateTime(DateTime.Now);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
