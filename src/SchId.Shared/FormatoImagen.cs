namespace SchId.Shared;

public enum TipoImagen
{
    Desconocido,
    Jpeg,
    Png,
    Bmp,
    Gif,
    Tiff
}

/// <summary>Dónde empieza la imagen dentro del blob y de qué tipo es.</summary>
public readonly record struct ImagenDetectada(TipoImagen Tipo, int Desplazamiento)
{
    public static readonly ImagenDetectada Ninguna = new(TipoImagen.Desconocido, -1);

    public bool Encontrada => Tipo != TipoImagen.Desconocido;

    /// <summary>Si hubo que saltarse una cabecera para llegar a la imagen.</summary>
    public bool ConCabecera => Encontrada && Desplazamiento > 0;
}

/// <summary>
/// Reconoce el formato de una imagen por sus bytes, y encuentra dónde empieza
/// de verdad dentro del blob.
///
/// ## Por qué hace falta buscar y no solo mirar el principio
///
/// Las imágenes del sistema anterior vienen de campos BLOB de Paradox bajo BDE,
/// y ahí es habitual que el motor —o el propio Delphi al guardar un TPicture—
/// anteponga una cabecera antes de los datos de la imagen. El blob entonces no
/// empieza por la firma del formato, aunque lleve una imagen perfectamente
/// válida unos bytes más adelante.
///
/// Por eso se busca la firma en los primeros bytes en lugar de exigirla en la
/// posición cero. Se limita la búsqueda a [MaximoDesplazamiento] porque una
/// firma encontrada en mitad de los datos no sería el inicio de nada: sería una
/// coincidencia.
/// </summary>
public static class FormatoImagen
{
    /// <summary>
    /// Hasta dónde se busca la firma. Las cabeceras conocidas (Paradox, OLE,
    /// nombre de clase de Delphi) son de unas pocas decenas de bytes; 512 deja
    /// margen de sobra sin llegar a los datos de la imagen.
    /// </summary>
    public const int MaximoDesplazamiento = 512;

    private static readonly (TipoImagen Tipo, byte[] Firma)[] Firmas =
    {
        (TipoImagen.Jpeg, new byte[] { 0xFF, 0xD8, 0xFF }),
        (TipoImagen.Png, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        (TipoImagen.Gif, new byte[] { 0x47, 0x49, 0x46, 0x38 }),
        (TipoImagen.Tiff, new byte[] { 0x49, 0x49, 0x2A, 0x00 }),
        (TipoImagen.Tiff, new byte[] { 0x4D, 0x4D, 0x00, 0x2A })
    };

    /// <summary>Solo mira el principio, sin buscar cabeceras.</summary>
    public static TipoImagen Reconocer(ReadOnlySpan<byte> contenido) =>
        EmpiezaAqui(contenido, 0);

    /// <summary>
    /// Busca dónde empieza la imagen, saltándose una posible cabecera.
    /// </summary>
    public static ImagenDetectada Detectar(ReadOnlySpan<byte> contenido)
    {
        if (contenido.Length == 0)
        {
            return ImagenDetectada.Ninguna;
        }

        var tope = Math.Min(contenido.Length, MaximoDesplazamiento);

        for (var i = 0; i < tope; i++)
        {
            var tipo = EmpiezaAqui(contenido, i);
            if (tipo != TipoImagen.Desconocido)
            {
                return new ImagenDetectada(tipo, i);
            }
        }

        return ImagenDetectada.Ninguna;
    }

    /// <summary>
    /// Devuelve la imagen sin la cabecera que la precediera. Si no se reconoce
    /// nada, devuelve el contenido tal cual: es mejor dejar que quien lo use
    /// falle con los bytes originales que entregarle un recorte inventado.
    /// </summary>
    public static ReadOnlyMemory<byte> SinCabecera(ReadOnlyMemory<byte> contenido)
    {
        var detectada = Detectar(contenido.Span);
        return detectada.ConCabecera ? contenido[detectada.Desplazamiento..] : contenido;
    }

    private static TipoImagen EmpiezaAqui(ReadOnlySpan<byte> contenido, int posicion)
    {
        var resto = contenido[posicion..];

        foreach (var (tipo, firma) in Firmas)
        {
            if (resto.Length >= firma.Length && resto[..firma.Length].SequenceEqual(firma))
            {
                return tipo;
            }
        }

        return EsBmp(resto, esInicioDelBlob: posicion == 0) ? TipoImagen.Bmp : TipoImagen.Desconocido;
    }

    /// <summary>
    /// BMP se identifica solo por "BM", dos bytes: demasiado poco para buscarlo
    /// dentro de un blob sin toparse con coincidencias. Se comprueba además que
    /// el tamaño declarado en la cabecera cuadre con lo que queda, que es lo que
    /// convierte la coincidencia en una identificación.
    /// </summary>
    private static bool EsBmp(ReadOnlySpan<byte> contenido, bool esInicioDelBlob)
    {
        if (contenido.Length < 14 || contenido[0] != 0x42 || contenido[1] != 0x4D)
        {
            return false;
        }

        var declarado = contenido[2] | (contenido[3] << 8) | (contenido[4] << 16) | (contenido[5] << 24);

        if (declarado == contenido.Length)
        {
            return true;
        }

        // Algunos escritores dejan el tamaño en cero. Se acepta únicamente al
        // principio del blob: en mitad de unos datos, "BM" seguido de cuatro
        // ceros es una coincidencia de lo más corriente.
        return declarado == 0 && esInicioDelBlob;
    }

    public static string Nombre(TipoImagen tipo) => tipo switch
    {
        TipoImagen.Jpeg => "JPEG",
        TipoImagen.Png => "PNG",
        TipoImagen.Bmp => "BMP",
        TipoImagen.Gif => "GIF",
        TipoImagen.Tiff => "TIFF",
        _ => "desconocido"
    };
}
