using SchId.Shared;

namespace SchId.Api.Services;

public interface IImageStorageService
{
    /// <summary>Guarda el contenido en disco y devuelve la ruta completa donde quedó.</summary>
    Task<string> SaveAsync(long personaId, ImageSide side, Stream content, CancellationToken ct = default);

    /// <summary>Ruta completa calculada para la imagen (exista o no el archivo todavía).</summary>
    string GetPath(long personaId, ImageSide side);

    bool Exists(long personaId, ImageSide side);

    void Delete(long personaId, ImageSide side);
}
