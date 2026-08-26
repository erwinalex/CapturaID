using SchId.Shared;

namespace SchId.Tests;

/// <summary>
/// Reconocer el formato importa por un caso real: en una ubicación, una persona
/// con las fotos en las columnas del sistema anterior salía en la ficha con "no
/// se pudo recuperar la imagen". Los bytes estaban ahí; lo que fallaba era
/// decodificarlos.
/// </summary>
public class FormatoImagenTests
{
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6 };
    private static readonly byte[] Gif = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 2, 3, 4, 5, 6, 7, 8 };

    private static byte[] ConCabecera(byte[] imagen, int bytes)
    {
        // Una cabecera plausible: bytes cualesquiera que no forman ninguna
        // firma, como los que anteponen los BLOB de Paradox.
        var cabecera = Enumerable.Range(1, bytes).Select(i => (byte)(i % 251)).ToArray();
        return cabecera.Concat(imagen).ToArray();
    }

    private static byte[] Bmp(int tamano)
    {
        var bmp = new byte[tamano];
        bmp[0] = 0x42;
        bmp[1] = 0x4D;
        BitConverter.GetBytes(tamano).CopyTo(bmp, 2);
        return bmp;
    }

    [Fact]
    public void Se_reconocen_los_formatos_por_su_firma()
    {
        Assert.Equal(TipoImagen.Jpeg, FormatoImagen.Reconocer(Jpeg));
        Assert.Equal(TipoImagen.Png, FormatoImagen.Reconocer(Png));
        Assert.Equal(TipoImagen.Gif, FormatoImagen.Reconocer(Gif));
        Assert.Equal(TipoImagen.Bmp, FormatoImagen.Reconocer(Bmp(60)));
    }

    [Fact]
    public void Lo_que_no_es_una_imagen_no_se_reconoce()
    {
        Assert.Equal(TipoImagen.Desconocido, FormatoImagen.Reconocer(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Equal(TipoImagen.Desconocido, FormatoImagen.Reconocer(Array.Empty<byte>()));
    }

    /// <summary>
    /// El caso que motivó todo esto: la imagen es válida pero no empieza en el
    /// byte cero porque el BLOB trae una cabecera delante.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(47)]
    [InlineData(78)]
    public void Una_imagen_precedida_de_una_cabecera_se_encuentra(int bytesDeCabecera)
    {
        var blob = ConCabecera(Jpeg, bytesDeCabecera);

        var detectada = FormatoImagen.Detectar(blob);

        Assert.Equal(TipoImagen.Jpeg, detectada.Tipo);
        Assert.Equal(bytesDeCabecera, detectada.Desplazamiento);
        Assert.True(detectada.ConCabecera);
        Assert.Equal(Jpeg, FormatoImagen.SinCabecera(blob).ToArray());
    }

    [Fact]
    public void Una_imagen_sin_cabecera_se_deja_intacta()
    {
        var detectada = FormatoImagen.Detectar(Jpeg);

        Assert.False(detectada.ConCabecera);
        Assert.Equal(Jpeg, FormatoImagen.SinCabecera(Jpeg).ToArray());
    }

    /// <summary>
    /// Si no se reconoce nada, se devuelve el contenido tal cual: entregar un
    /// recorte inventado sería peor que fallar con los bytes originales.
    /// </summary>
    [Fact]
    public void Sin_firma_reconocible_no_se_recorta_nada()
    {
        var basura = Enumerable.Range(0, 300).Select(i => (byte)(i % 97)).ToArray();

        Assert.False(FormatoImagen.Detectar(basura).Encontrada);
        Assert.Equal(basura, FormatoImagen.SinCabecera(basura).ToArray());
    }

    /// <summary>
    /// Una firma muy adentro del blob no es el principio de nada: es una
    /// coincidencia entre los datos comprimidos de otra imagen.
    /// </summary>
    [Fact]
    public void Una_firma_demasiado_adentro_no_cuenta()
    {
        var blob = ConCabecera(Jpeg, FormatoImagen.MaximoDesplazamiento + 50);

        Assert.False(FormatoImagen.Detectar(blob).Encontrada);
    }

    /// <summary>
    /// "BM" son dos bytes y aparecen por casualidad todo el tiempo. Sin
    /// comprobar el tamaño declarado, cualquier blob con esas letras se leería
    /// como un BMP y se recortaría por donde no es.
    /// </summary>
    [Fact]
    public void Un_BM_suelto_dentro_de_los_datos_no_se_toma_por_un_BMP()
    {
        var blob = new byte[200];
        for (var i = 0; i < blob.Length; i++)
        {
            blob[i] = (byte)(i % 89 + 1);
        }

        // "BM" en mitad de los datos, con un tamaño que no cuadra con nada.
        blob[100] = 0x42;
        blob[101] = 0x4D;
        BitConverter.GetBytes(123456).CopyTo(blob, 102);

        Assert.False(FormatoImagen.Detectar(blob).Encontrada);
    }

    [Fact]
    public void Un_BMP_de_verdad_precedido_de_cabecera_si_se_encuentra()
    {
        var blob = ConCabecera(Bmp(120), 30);

        var detectada = FormatoImagen.Detectar(blob);

        Assert.Equal(TipoImagen.Bmp, detectada.Tipo);
        Assert.Equal(30, detectada.Desplazamiento);
    }
}
