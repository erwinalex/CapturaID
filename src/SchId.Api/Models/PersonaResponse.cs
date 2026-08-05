using SchId.Api.Data.Entities;

namespace SchId.Api.Models;

public class PersonaResponse
{
    public long Id { get; set; }
    public string? Nombre { get; set; }
    public string? Curp { get; set; }
    public string? Direccion { get; set; }

    // Los campos nchar vienen rellenados con espacios desde SQL Server
    // (longitud fija) - por eso se hace Trim() al exponerlos hacia afuera.
    public static PersonaResponse DesdeEntidad(Persona p) => new()
    {
        Id = p.ID,
        Nombre = p.Nombre?.Trim(),
        Curp = p.CURP?.Trim(),
        Direccion = p.Direccion?.Trim()
    };
}
