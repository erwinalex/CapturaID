using SchId.Shared;

namespace SchId.Consulta.Nucleo;

/// <summary>
/// Un renglón del listado de búsqueda. No trae imágenes: cargarlas para toda
/// una lista de resultados sería traer megabytes por una búsqueda que casi
/// siempre termina en una sola persona.
/// </summary>
public sealed record ResultadoBusqueda
{
    public required long Id { get; init; }
    public string? Nombre { get; init; }
    public string? Curp { get; init; }
    public string? Nacionalidad { get; init; }
    public int? Edad { get; init; }
    public DateTime? FechaAlta { get; init; }
}

/// <summary>
/// Todo lo que va en la ficha de una persona, ya con las fechas convertidas
/// desde el TDateTime de Delphi.
/// </summary>
public sealed record FichaPersona
{
    public required long Id { get; init; }
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

internal static class ConversionFechas
{
    public static DateTime? Delphi(decimal? valor) => DelphiDateTime.ToDateTime(valor);
}
