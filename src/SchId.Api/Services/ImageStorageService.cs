using Microsoft.Extensions.Options;
using SchId.Shared;

namespace SchId.Api.Services;

public class ImageStorageOptions
{
    /// <summary>Carpeta base donde se guardan las imágenes. Configurable en appsettings.json.</summary>
    public string BasePath { get; set; } = "";

    /// <summary>Tamaño máximo aceptado por imagen. Una foto de INE de CameraX ronda 1-3 MB.</summary>
    public int MaxBytesPorImagen { get; set; } = 8 * 1024 * 1024;
}

/// <summary>
/// Guarda y localiza las imágenes de la INE como archivos en disco, nombrados de
/// forma predecible a partir del Id de la persona (ver ImagePathHelper). No se
/// guarda ninguna ruta en la base de datos: se calcula siempre en código.
/// </summary>
public class ImageStorageService : IImageStorageService
{
    private readonly string _basePath;

    public ImageStorageService(IOptions<ImageStorageOptions> options)
    {
        if (string.IsNullOrWhiteSpace(options.Value.BasePath))
        {
            throw new InvalidOperationException(
                "Falta configurar ImageStorage:BasePath en appsettings.json.");
        }

        _basePath = options.Value.BasePath;
        Directory.CreateDirectory(_basePath);
    }

    public string GetPath(long personaId, ImageSide side) =>
        ImagePathHelper.GetFullPath(_basePath, personaId, side);

    public bool Exists(long personaId, ImageSide side) =>
        File.Exists(GetPath(personaId, side));

    /// <summary>
    /// Escribe primero a un archivo temporal y luego lo mueve sobre el definitivo.
    /// Si la subida se corta a la mitad (el kiosko pierde la red, se reinicia),
    /// lo que queda roto es el temporal y la imagen buena que ya estaba guardada
    /// sigue intacta. Escribir directo sobre el destino la dejaría truncada.
    /// </summary>
    public async Task<string> SaveAsync(long personaId, ImageSide side, Stream content, CancellationToken ct = default)
    {
        var path = GetPath(personaId, side);
        var temporal = path + ".tmp";

        try
        {
            await using (var fileStream = File.Create(temporal))
            {
                await content.CopyToAsync(fileStream, ct);
            }

            File.Move(temporal, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporal))
            {
                File.Delete(temporal);
            }

            throw;
        }

        return path;
    }

    public void Delete(long personaId, ImageSide side)
    {
        var path = GetPath(personaId, side);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
