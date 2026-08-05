using SchId.Api.Data.Entities;
using SchId.Shared;

namespace SchId.Api.Models;

/// <summary>
/// Datos completos de una persona. Solo se entrega a tokens con el rol Consulta
/// (soporte / herramientas administrativas); el kiosko no puede pedir esto.
/// </summary>
public class PersonaResponse
{
    public long Id { get; set; }
    public string? Nombre { get; set; }
    public string? Curp { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Nacionalidad { get; set; }
    public int? Edad { get; set; }
    public string? Residencia { get; set; }

    /// <summary>Ya convertida desde el TDateTime de Delphi que guarda la columna.</summary>
    public DateTime? FechaAlta { get; set; }

    /// <summary>Ya convertida desde el TDateTime de Delphi que guarda la columna.</summary>
    public DateTime? UltimaModificacion { get; set; }

    public bool TieneImagenFrente { get; set; }
    public bool TieneImagenReverso { get; set; }

    // Los campos nchar vienen rellenados con espacios desde SQL Server
    // (longitud fija) - por eso se hace Trim() al exponerlos hacia afuera.
    public static PersonaResponse DesdeEntidad(Persona p) => new()
    {
        Id = p.ID,
        Nombre = p.Nombre?.Trim(),
        Curp = p.CURP?.Trim(),
        Direccion = p.Direccion?.Trim(),
        Telefono = p.Telefono?.Trim(),
        Nacionalidad = p.Nacionalidad?.Trim(),
        Edad = p.Edad,
        Residencia = p.Residencia?.Trim(),
        FechaAlta = DelphiDateTime.ToDateTime(p.FechaAlta),
        UltimaModificacion = DelphiDateTime.ToDateTime(p.UltimaModificacion)
    };
}
