using SchId.Api.Data.Entities;
using SchId.Shared;

namespace SchId.Api.Models;

/// <summary>
/// Una estancia de la persona, tal como la registró el PMS.
///
/// Esta API **no escribe** en dbo.Estancias —las estancias son del PMS— pero sí
/// las lee: para el job de retención, y ahora para poder mostrarlas junto a la
/// identificación de quien se consulta.
/// </summary>
public class EstanciaResponse
{
    public long IdEstancia { get; set; }

    /// <summary>Ya convertida desde el TDateTime de Delphi. Null si nunca se llenó.</summary>
    public DateTime? Ingreso { get; set; }

    /// <summary>
    /// Ya convertida. Null cuando la estancia sigue abierta **o** cuando el
    /// campo nunca se llenó: desde aquí no se distinguen, porque en la columna
    /// se ven igual.
    /// </summary>
    public DateTime? Salida { get; set; }

    public DateTime? Deteccion { get; set; }

    /// <summary>Bandera del PMS. Se entrega tal cual, sin interpretarla.</summary>
    public int? Hospedado { get; set; }

    public string? TipoAsignacion { get; set; }

    public static EstanciaResponse DesdeEntidad(Estancia e) => new()
    {
        IdEstancia = e.IdEstancia,
        // ToDateTimeSiEsReal y no ToDateTime: en estas columnas un 0 significa
        // "nunca se llenó", y convertirlo daría 1899-12-30 en pantalla.
        Ingreso = DelphiDateTime.ToDateTimeSiEsReal(e.FIngreso),
        Salida = DelphiDateTime.ToDateTimeSiEsReal(e.FSalida),
        Deteccion = DelphiDateTime.ToDateTimeSiEsReal(e.FDeteccion),
        Hospedado = e.Hospedado,
        // nchar de largo fijo: llega relleno de espacios.
        TipoAsignacion = e.TipoAsignacion?.Trim()
    };
}
