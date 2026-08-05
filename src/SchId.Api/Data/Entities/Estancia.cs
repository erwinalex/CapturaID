namespace SchId.Api.Data.Entities;

/// <summary>Mapea la tabla dbo.Estancias existente (registro de entradas/salidas).</summary>
public class Estancia
{
    public long IdEstancia { get; set; }
    public int? AccId { get; set; }

    /// <summary>Numeric(18,8) tipo TDateTime de Delphi. Usar DelphiDateTime para convertir.</summary>
    public decimal? FDeteccion { get; set; }

    /// <summary>Numeric(18,8) tipo TDateTime de Delphi. Usar DelphiDateTime para convertir.</summary>
    public decimal? FIngreso { get; set; }

    /// <summary>Numeric(18,8) tipo TDateTime de Delphi. Usar DelphiDateTime para convertir.</summary>
    public decimal? FSalida { get; set; }

    public int? Hospedado { get; set; }
    public long? IdPersona { get; set; }
    public string? TipoAsignacion { get; set; }
}
