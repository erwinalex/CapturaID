namespace SchId.Api.Services;

/// <summary>
/// Verificación mínima de que lo que subió el kiosko es de verdad un JPEG.
///
/// No se puede confiar en el Content-Type ni en el nombre del archivo: los manda
/// el cliente. Y como ImagePathHelper siempre nombra los archivos .jpg, si se
/// aceptara cualquier contenido acabaríamos con PNGs (o cualquier otra cosa)
/// guardados con extensión .jpg, que después nadie puede abrir. CameraX entrega
/// JPEG, así que exigir JPEG no le quita nada a la app.
/// </summary>
public static class ImagenJpeg
{
    /// <summary>Los primeros bytes de todo archivo JPEG: SOI (FF D8) + inicio de marcador (FF).</summary>
    public static bool EsJpeg(ReadOnlySpan<byte> contenido)
    {
        return contenido.Length >= 3
            && contenido[0] == 0xFF
            && contenido[1] == 0xD8
            && contenido[2] == 0xFF;
    }
}
