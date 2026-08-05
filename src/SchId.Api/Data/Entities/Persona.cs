namespace SchId.Api.Data.Entities;

/// <summary>
/// Mapea la tabla dbo.Personas existente. IDFoto1 e IDFoto2 (columnas [image])
/// NO se mapean aquí a propósito: desde este código ya no se lee ni se escribe
/// binario ahí. Las imágenes nuevas se guardan como archivo (ver
/// IImageStorageService) y las antiguas se migran con la herramienta
/// SchId.ImageMigration.
/// </summary>
public class Persona
{
    public long ID { get; set; }
    public string? Nombre { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Nacionalidad { get; set; }
    public int? Edad { get; set; }
    public string? Residencia { get; set; }

    // Campo histórico "Ocupación": ya NO se usa para guardar rutas de imagen
    // (ver decisión de diseño: las rutas se calculan a partir del ID, no se
    // guardan en la base). Se deja mapeado por si el campo sigue en uso para
    // su propósito original en otras partes del sistema.
    public string? Ocupacion { get; set; }

    public string? CURP { get; set; }
    public string? Origen { get; set; }
    public string? IdAWS { get; set; }
    public long? IdAWSInt { get; set; }
    public int? AutorizoFaceID { get; set; }

    /// <summary>Numeric(18,1) tipo TDateTime de Delphi. Usar DelphiDateTime para convertir.</summary>
    public decimal? FechaAlta { get; set; }

    /// <summary>Numeric(18,10) tipo TDateTime de Delphi. Usar DelphiDateTime para convertir.</summary>
    public decimal? UltimaModificacion { get; set; }
}
