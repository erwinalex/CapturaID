using Microsoft.Extensions.Options;
using SchId.Api.Services;
using SchId.Shared;

namespace SchId.Tests;

public class ImagePathHelperTests
{
    [Theory]
    [InlineData(1, ImageSide.Frente, "1_frente.jpg")]
    [InlineData(1, ImageSide.Reverso, "1_reverso.jpg")]
    [InlineData(987654, ImageSide.Frente, "987654_frente.jpg")]
    public void El_nombre_de_archivo_se_deriva_del_id(long id, ImageSide lado, string esperado)
    {
        Assert.Equal(esperado, ImagePathHelper.GetFileName(id, lado));
    }

    /// <summary>
    /// La API y la herramienta de migración calculan el nombre por separado. Si
    /// dejaran de coincidir, la API no encontraría las imágenes migradas.
    /// </summary>
    [Fact]
    public void La_ruta_completa_combina_la_carpeta_base_con_ese_nombre()
    {
        var ruta = ImagePathHelper.GetFullPath("/datos/ine", 42, ImageSide.Reverso);

        Assert.Equal(Path.Combine("/datos/ine", "42_reverso.jpg"), ruta);
    }
}

public class ImagenJpegTests
{
    [Fact]
    public void Reconoce_la_firma_de_un_jpeg()
    {
        Assert.True(ImagenJpeg.EsJpeg(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }));
    }

    [Fact]
    public void Rechaza_un_png_aunque_venga_con_nombre_de_jpg()
    {
        Assert.False(ImagenJpeg.EsJpeg(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF, 0xD8 })]
    public void Rechaza_contenido_demasiado_corto_para_ser_valido(byte[] contenido)
    {
        Assert.False(ImagenJpeg.EsJpeg(contenido));
    }
}

public class ImageStorageServiceTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(), "schid-tests-" + Guid.NewGuid().ToString("N"));

    private ImageStorageService CrearServicio() =>
        new(Options.Create(new ImageStorageOptions { BasePath = _carpeta }));

    [Fact]
    public async Task Guarda_la_imagen_y_no_deja_archivos_temporales()
    {
        var servicio = CrearServicio();
        var contenido = new byte[] { 0xFF, 0xD8, 0xFF, 0x01, 0x02 };

        await servicio.SaveAsync(10, ImageSide.Frente, new MemoryStream(contenido));

        Assert.True(servicio.Exists(10, ImageSide.Frente));
        Assert.Equal(contenido, await File.ReadAllBytesAsync(servicio.GetPath(10, ImageSide.Frente)));
        Assert.Empty(Directory.GetFiles(_carpeta, "*.tmp"));
    }

    /// <summary>
    /// La razón de escribir a un temporal y mover: si la subida se corta a la
    /// mitad, la imagen que ya estaba guardada no debe quedar truncada.
    /// </summary>
    [Fact]
    public async Task Una_subida_que_falla_no_daña_la_imagen_anterior()
    {
        var servicio = CrearServicio();
        var buena = new byte[] { 0xFF, 0xD8, 0xFF, 0xAA, 0xBB };
        await servicio.SaveAsync(10, ImageSide.Frente, new MemoryStream(buena));

        await Assert.ThrowsAsync<IOException>(() =>
            servicio.SaveAsync(10, ImageSide.Frente, new StreamQueFalla()));

        Assert.Equal(buena, await File.ReadAllBytesAsync(servicio.GetPath(10, ImageSide.Frente)));
        Assert.Empty(Directory.GetFiles(_carpeta, "*.tmp"));
    }

    [Fact]
    public async Task Una_captura_nueva_reemplaza_la_imagen_anterior()
    {
        var servicio = CrearServicio();
        await servicio.SaveAsync(10, ImageSide.Frente, new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0x01 }));

        var nueva = new byte[] { 0xFF, 0xD8, 0xFF, 0x09, 0x09, 0x09 };
        await servicio.SaveAsync(10, ImageSide.Frente, new MemoryStream(nueva));

        Assert.Equal(nueva, await File.ReadAllBytesAsync(servicio.GetPath(10, ImageSide.Frente)));
    }

    [Fact]
    public async Task Delete_borra_la_imagen_y_no_falla_si_ya_no_estaba()
    {
        var servicio = CrearServicio();
        await servicio.SaveAsync(10, ImageSide.Reverso, new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF }));

        servicio.Delete(10, ImageSide.Reverso);
        servicio.Delete(10, ImageSide.Reverso);

        Assert.False(servicio.Exists(10, ImageSide.Reverso));
    }

    [Fact]
    public void Sin_BasePath_configurado_no_arranca()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ImageStorageService(Options.Create(new ImageStorageOptions { BasePath = "  " })));
    }

    public void Dispose()
    {
        if (Directory.Exists(_carpeta))
        {
            Directory.Delete(_carpeta, recursive: true);
        }
    }

    /// <summary>Simula que el kiosko pierde la red a media subida.</summary>
    private class StreamQueFalla : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Conexión interrumpida.");

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
