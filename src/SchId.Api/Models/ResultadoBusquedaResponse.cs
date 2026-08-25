using SchId.Api.Data.Entities;
using SchId.Shared;

namespace SchId.Api.Models;

/// <summary>
/// Un renglón del listado de búsqueda.
///
/// Deliberadamente más corto que <see cref="PersonaResponse"/>: no lleva
/// domicilio ni teléfono. Una búsqueda por apellido puede devolver doscientas
/// personas, y no hay razón para poner el domicilio de doscientos desconocidos
/// en la pantalla de quien solo busca a uno.
/// </summary>
public class ResultadoBusquedaResponse
{
    public long Id { get; set; }
    public string? Nombre { get; set; }
    public string? Curp { get; set; }
    public string? Nacionalidad { get; set; }
    public int? Edad { get; set; }

    /// <summary>Ya convertida desde el TDateTime de Delphi que guarda la columna.</summary>
    public DateTime? FechaAlta { get; set; }

    // Los campos nchar vienen rellenados con espacios desde SQL Server
    // (longitud fija) - por eso se hace Trim() al exponerlos hacia afuera.
    public static ResultadoBusquedaResponse DesdeEntidad(Persona p) => new()
    {
        Id = p.ID,
        Nombre = p.Nombre?.Trim(),
        Curp = p.CURP?.Trim(),
        Nacionalidad = p.Nacionalidad?.Trim(),
        Edad = p.Edad,
        FechaAlta = DelphiDateTime.ToDateTime(p.FechaAlta)
    };
}
