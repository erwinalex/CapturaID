namespace SchId.Shared;

public enum ImageSide
{
    Frente,
    Reverso
}

/// <summary>
/// Calcula de forma predecible el nombre y la ruta de archivo de las imágenes de
/// la INE a partir del Id de la persona, para no tener que guardar ninguna ruta
/// en la base de datos. Usada tanto por la API (al guardar/leer imágenes nuevas)
/// como por la herramienta de migración (al extraer las imágenes que ya existen
/// como binario en Personas.IDFoto1 / IDFoto2), para que ambas generen
/// exactamente el mismo nombre de archivo.
/// </summary>
public static class ImagePathHelper
{
    public static string GetFileName(long personaId, ImageSide side)
    {
        var sufijo = side == ImageSide.Frente ? "frente" : "reverso";
        return $"{personaId}_{sufijo}.jpg";
    }

    public static string GetFullPath(string basePath, long personaId, ImageSide side)
    {
        return Path.Combine(basePath, GetFileName(personaId, side));
    }
}
