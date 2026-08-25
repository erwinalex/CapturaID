using Microsoft.Data.SqlClient;
using SchId.Shared;

namespace SchId.Api.Services;

/// <summary>
/// Las imágenes que el sistema anterior dejó como binario en las columnas
/// IDFoto1 / IDFoto2 de dbo.Personas.
///
/// Se declara como interfaz porque la entidad Persona no mapea esas columnas a
/// propósito (ver Persona) y hay que leerlas con SQL crudo, que el proveedor en
/// memoria de las pruebas no soporta.
/// </summary>
public interface IImagenesHeredadas
{
    Task<byte[]?> LeerAsync(long personaId, ImageSide lado, CancellationToken ct = default);
}

public sealed class ImagenesHeredadasSql : IImagenesHeredadas
{
    private readonly string _cadenaDeConexion;

    public ImagenesHeredadasSql(IConfiguration configuracion)
    {
        _cadenaDeConexion = configuracion.GetConnectionString("SchIdDatabase")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:SchIdDatabase.");
    }

    public async Task<byte[]?> LeerAsync(long personaId, ImageSide lado, CancellationToken ct = default)
    {
        // El nombre de columna sale de un enum de dos valores y se resuelve
        // aquí, así que no hay forma de inyectar SQL por esta vía.
        var columna = lado == ImageSide.Frente ? "IDFoto1" : "IDFoto2";

        await using var conexion = new SqlConnection(_cadenaDeConexion);
        await conexion.OpenAsync(ct);

        await using var comando = new SqlCommand(
            $"SELECT {columna} FROM dbo.Personas WHERE ID = @id", conexion);
        comando.Parameters.AddWithValue("@id", personaId);

        var valor = await comando.ExecuteScalarAsync(ct);
        return valor is byte[] bytes && bytes.Length > 0 ? bytes : null;
    }
}

/// <summary>De dónde salió la imagen que se entregó.</summary>
public enum OrigenImagen
{
    Archivo,
    ColumnaDeBaseDeDatos
}

public sealed record ImagenLocalizada(byte[] Contenido, OrigenImagen Origen);

/// <summary>
/// Busca la imagen de una identificación donde toque.
///
/// Conviven dos formas de guardarlas: la app móvil deja un archivo en disco y el
/// sistema anterior guarda el binario en la columna. Durante la transición hay
/// personas de un tipo y de otro en la misma tabla.
///
/// **El archivo manda.** La herramienta de migración escribe el archivo y solo
/// entonces vacía la columna, así que cuando existen los dos, el archivo es el
/// que ya se dio por bueno. Preferir la columna mostraría la copia vieja de
/// alguien que ya se migró.
///
/// Vive en el servidor —y no en quien consulta— porque los archivos están en el
/// disco del servidor. Una aplicación de escritorio en otra PC no puede leer esa
/// carpeta sin un recurso compartido de Windows, y montar un compartido con
/// imágenes de INE para que lo lea cada terminal es peor que servirlas por aquí.
/// </summary>
public sealed class ResolutorImagenesIne
{
    private readonly IImageStorageService _archivos;
    private readonly IImagenesHeredadas _heredadas;

    public ResolutorImagenesIne(IImageStorageService archivos, IImagenesHeredadas heredadas)
    {
        _archivos = archivos;
        _heredadas = heredadas;
    }

    public async Task<ImagenLocalizada?> ObtenerAsync(long personaId, ImageSide lado, CancellationToken ct = default)
    {
        if (_archivos.Exists(personaId, lado))
        {
            var ruta = _archivos.GetPath(personaId, lado);

            try
            {
                var contenido = await File.ReadAllBytesAsync(ruta, ct);

                // Un archivo de cero bytes es una escritura que salió mal, no
                // una imagen: mejor caer a la columna que devolver nada.
                if (contenido.Length > 0)
                {
                    return new ImagenLocalizada(contenido, OrigenImagen.Archivo);
                }
            }
            catch (IOException)
            {
                // Un permiso o un disco con problemas no deben dejar sin ficha a
                // quien sí tiene la imagen en la columna.
            }
        }

        var deColumna = await _heredadas.LeerAsync(personaId, lado, ct);
        return deColumna is { Length: > 0 }
            ? new ImagenLocalizada(deColumna, OrigenImagen.ColumnaDeBaseDeDatos)
            : null;
    }
}
