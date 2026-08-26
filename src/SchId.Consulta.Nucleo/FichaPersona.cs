namespace SchId.Consulta.Nucleo;

/// <summary>
/// Un renglón del listado de búsqueda. No trae imágenes ni domicilio: cargarlos
/// para toda una lista sería traer megabytes —y poner el domicilio de doscientos
/// desconocidos en pantalla— por una búsqueda que casi siempre termina en una
/// sola persona.
/// </summary>
public sealed record ResultadoBusqueda
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public string? Curp { get; init; }
    public string? Nacionalidad { get; init; }
    public int? Edad { get; init; }
    public DateTime? FechaAlta { get; init; }
}

/// <summary>
/// Todo lo que va en la ficha de una persona. Las fechas llegan ya convertidas
/// desde el TDateTime de Delphi: eso lo hace el servidor, que es donde vive
/// DelphiDateTime y su desfase de dos días.
/// </summary>
public sealed record FichaPersona
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public string? Curp { get; init; }
    public string? Direccion { get; init; }
    public string? Telefono { get; init; }
    public string? Nacionalidad { get; init; }
    public int? Edad { get; init; }
    public string? Residencia { get; init; }
    public string? Origen { get; init; }
    public DateTime? FechaAlta { get; init; }
    public DateTime? UltimaModificacion { get; init; }

    public ImagenIdentificacion? Frente { get; init; }
    public ImagenIdentificacion? Reverso { get; init; }
}

/// <summary>
/// De dónde salió una imagen.
///
/// Importa mostrarlo: durante la transición conviven las capturas de la app
/// móvil —que dejan archivo— con las del sistema anterior —que dejan binario en
/// la columna—, y saber cuál es cuál es lo que permite comprobar que la
/// migración va bien sin abrir la base a mano.
/// </summary>
public enum OrigenImagen
{
    Archivo,
    ColumnaDeBaseDeDatos
}

public sealed record ImagenIdentificacion(byte[] Contenido, OrigenImagen Origen, string Descripcion);

/// <summary>
/// Una estancia registrada por el PMS. La consulta solo las lee.
/// </summary>
public sealed record Estancia
{
    public long IdEstancia { get; init; }
    public DateTime? Ingreso { get; init; }

    /// <summary>
    /// Null cuando la estancia sigue abierta **o** cuando el campo nunca se
    /// llenó: en la columna se ven igual y desde aquí no se distinguen.
    /// </summary>
    public DateTime? Salida { get; init; }

    public DateTime? Deteccion { get; init; }

    /// <summary>Bandera del PMS, tal cual viene.</summary>
    public int? Hospedado { get; init; }

    public string? TipoAsignacion { get; init; }
}
