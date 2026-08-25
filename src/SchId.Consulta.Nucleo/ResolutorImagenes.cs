using Microsoft.Data.SqlClient;
using SchId.Shared;

namespace SchId.Consulta.Nucleo;

/// <summary>Un sitio donde puede estar guardada la imagen de una identificación.</summary>
public interface IFuenteDeImagenes
{
    Task<byte[]?> LeerAsync(long personaId, ImageSide lado, CancellationToken cancelacion = default);
}

/// <summary>
/// Busca la imagen donde toque.
///
/// Conviven dos formas de guardarlas y esta aplicación tiene que leer las dos:
/// la app móvil deja un archivo en disco, y el sistema anterior guarda el
/// binario en las columnas IDFoto1 / IDFoto2. Durante la transición hay
/// personas de un tipo y de otro en la misma tabla.
///
/// **El archivo manda.** La herramienta de migración escribe el archivo y solo
/// entonces vacía la columna, así que cuando existen los dos, el archivo es el
/// que ya se dio por bueno. Preferir la columna llevaría a mostrar la copia
/// vieja de alguien que ya se migró.
///
/// Un archivo vacío no cuenta como imagen: si algo salió mal al escribirlo, es
/// mejor caer a la columna que enseñar una ficha sin identificación.
/// </summary>
public sealed class ResolutorImagenes
{
    private readonly IFuenteDeImagenes _archivos;
    private readonly IFuenteDeImagenes _baseDeDatos;

    public ResolutorImagenes(IFuenteDeImagenes archivos, IFuenteDeImagenes baseDeDatos)
    {
        _archivos = archivos;
        _baseDeDatos = baseDeDatos;
    }

    public async Task<ImagenIdentificacion?> ObtenerAsync(
        long personaId,
        ImageSide lado,
        CancellationToken cancelacion = default)
    {
        var deArchivo = await _archivos.LeerAsync(personaId, lado, cancelacion);
        if (deArchivo is { Length: > 0 })
        {
            return new ImagenIdentificacion(
                deArchivo,
                OrigenImagen.Archivo,
                $"archivo {ImagePathHelper.GetFileName(personaId, lado)}");
        }

        var deBase = await _baseDeDatos.LeerAsync(personaId, lado, cancelacion);
        if (deBase is { Length: > 0 })
        {
            return new ImagenIdentificacion(
                deBase,
                OrigenImagen.ColumnaDeBaseDeDatos,
                $"columna {ColumnaDe(lado)} (sistema anterior, sin migrar)");
        }

        return null;
    }

    internal static string ColumnaDe(ImageSide lado) =>
        lado == ImageSide.Frente ? "IDFoto1" : "IDFoto2";
}

/// <summary>Las imágenes que dejó la app móvil, como archivo en disco.</summary>
public sealed class ImagenesEnArchivo : IFuenteDeImagenes
{
    private readonly string _rutaBase;

    public ImagenesEnArchivo(string rutaBase) => _rutaBase = rutaBase;

    public async Task<byte[]?> LeerAsync(long personaId, ImageSide lado, CancellationToken cancelacion = default)
    {
        var ruta = ImagePathHelper.GetFullPath(_rutaBase, personaId, lado);

        // Una carpeta mal configurada o un permiso faltante no deben tumbar la
        // consulta: se cae a la otra fuente y la ficha sale con lo que haya.
        try
        {
            return File.Exists(ruta) ? await File.ReadAllBytesAsync(ruta, cancelacion) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Las imágenes del sistema anterior, en las columnas [image] de dbo.Personas.
/// </summary>
public sealed class ImagenesEnBaseDeDatos : IFuenteDeImagenes
{
    private readonly string _cadenaDeConexion;

    public ImagenesEnBaseDeDatos(string cadenaDeConexion) => _cadenaDeConexion = cadenaDeConexion;

    public async Task<byte[]?> LeerAsync(long personaId, ImageSide lado, CancellationToken cancelacion = default)
    {
        // El nombre de columna no viene de fuera: sale de un enum de dos valores
        // y se resuelve aquí, así que no hay forma de inyectar SQL por esta vía.
        var columna = ResolutorImagenes.ColumnaDe(lado);

        await using var conexion = new SqlConnection(_cadenaDeConexion);
        await conexion.OpenAsync(cancelacion);

        await using var comando = new SqlCommand(
            $"SELECT {columna} FROM dbo.Personas WHERE ID = @id", conexion);
        comando.Parameters.AddWithValue("@id", personaId);

        var valor = await comando.ExecuteScalarAsync(cancelacion);
        return valor is byte[] bytes && bytes.Length > 0 ? bytes : null;
    }
}
