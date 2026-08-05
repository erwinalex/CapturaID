using Microsoft.Extensions.Options;
using SchId.Shared;

namespace SchId.Api.Services;

public class ImageStorageOptions
{
    /// <summary>Carpeta base donde se guardan las imágenes. Configurable en appsettings.json.</summary>
    public string BasePath { get; set; } = "";
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

    public async Task<string> SaveAsync(long personaId, ImageSide side, Stream content, CancellationToken ct = default)
    {
        var path = GetPath(personaId, side);
        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, ct);
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
