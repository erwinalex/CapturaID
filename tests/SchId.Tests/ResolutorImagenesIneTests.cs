using Microsoft.Extensions.Options;
using SchId.Api.Services;
using SchId.Shared;

namespace SchId.Tests;

/// <summary>
/// El requisito central de la consulta: hay personas capturadas con la app
/// móvil —imagen en archivo— y personas del sistema anterior —imagen en la
/// columna—, y la ficha tiene que salir bien en los dos casos.
/// </summary>
public class ResolutorImagenesIneTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(), "schid-resolutor-" + Guid.NewGuid().ToString("N"));

    private static readonly byte[] DeArchivo = { 1, 2, 3 };
    private static readonly byte[] DeColumna = { 9, 9, 9 };

    private sealed class HeredadasFalsas : IImagenesHeredadas
    {
        private readonly Dictionary<(long, ImageSide), byte[]?> _contenido = new();

        public int Lecturas { get; private set; }

        public HeredadasFalsas Con(long id, ImageSide lado, byte[]? bytes)
        {
            _contenido[(id, lado)] = bytes;
            return this;
        }

        public Task<byte[]?> LeerAsync(long personaId, ImageSide lado, CancellationToken ct = default)
        {
            Lecturas++;
            return Task.FromResult(_contenido.GetValueOrDefault((personaId, lado)));
        }
    }

    private ResolutorImagenesIne Crear(IImagenesHeredadas heredadas)
    {
        Directory.CreateDirectory(_carpeta);
        var almacen = new ImageStorageService(
            Options.Create(new ImageStorageOptions { BasePath = _carpeta }));

        return new ResolutorImagenesIne(almacen, heredadas);
    }

    private async Task EscribirArchivo(long id, ImageSide lado, byte[] contenido)
    {
        Directory.CreateDirectory(_carpeta);
        await File.WriteAllBytesAsync(ImagePathHelper.GetFullPath(_carpeta, id, lado), contenido);
    }

    public void Dispose()
    {
        if (Directory.Exists(_carpeta))
        {
            Directory.Delete(_carpeta, recursive: true);
        }
    }

    [Fact]
    public async Task Se_lee_del_archivo_cuando_lo_dejo_la_app_movil()
    {
        var resolutor = Crear(new HeredadasFalsas());
        await EscribirArchivo(7, ImageSide.Frente, DeArchivo);

        var imagen = await resolutor.ObtenerAsync(7, ImageSide.Frente);

        Assert.Equal(OrigenImagen.Archivo, imagen!.Origen);
        Assert.Equal(DeArchivo, imagen.Contenido);
    }

    [Fact]
    public async Task Se_cae_a_la_columna_cuando_la_persona_es_del_sistema_anterior()
    {
        var resolutor = Crear(new HeredadasFalsas().Con(7, ImageSide.Reverso, DeColumna));

        var imagen = await resolutor.ObtenerAsync(7, ImageSide.Reverso);

        Assert.Equal(OrigenImagen.ColumnaDeBaseDeDatos, imagen!.Origen);
        Assert.Equal(DeColumna, imagen.Contenido);
    }

    /// <summary>
    /// La herramienta de migración escribe el archivo y solo entonces vacía la
    /// columna, así que cuando existen los dos el archivo es el que ya se dio
    /// por bueno. Preferir la columna mostraría la copia vieja de alguien ya
    /// migrado.
    /// </summary>
    [Fact]
    public async Task Con_las_dos_fuentes_gana_el_archivo()
    {
        var resolutor = Crear(new HeredadasFalsas().Con(7, ImageSide.Frente, DeColumna));
        await EscribirArchivo(7, ImageSide.Frente, DeArchivo);

        var imagen = await resolutor.ObtenerAsync(7, ImageSide.Frente);

        Assert.Equal(DeArchivo, imagen!.Contenido);
    }

    /// <summary>
    /// Un archivo de cero bytes es una escritura que salió mal, no una imagen.
    /// Mejor la copia de la columna que una ficha sin identificación.
    /// </summary>
    [Fact]
    public async Task Un_archivo_vacio_no_cuenta_como_imagen()
    {
        var resolutor = Crear(new HeredadasFalsas().Con(7, ImageSide.Frente, DeColumna));
        await EscribirArchivo(7, ImageSide.Frente, Array.Empty<byte>());

        var imagen = await resolutor.ObtenerAsync(7, ImageSide.Frente);

        Assert.Equal(OrigenImagen.ColumnaDeBaseDeDatos, imagen!.Origen);
    }

    [Fact]
    public async Task Sin_imagen_en_ningun_lado_no_se_inventa_nada()
    {
        var resolutor = Crear(new HeredadasFalsas());

        Assert.Null(await resolutor.ObtenerAsync(7, ImageSide.Frente));
    }

    /// <summary>
    /// Ir a la base cuando el archivo ya respondió sería un viaje de red por
    /// cada cara de cada ficha, contra una columna [image] que pesa megabytes.
    /// </summary>
    [Fact]
    public async Task No_se_consulta_la_columna_si_el_archivo_ya_respondio()
    {
        var heredadas = new HeredadasFalsas().Con(7, ImageSide.Frente, DeColumna);
        var resolutor = Crear(heredadas);
        await EscribirArchivo(7, ImageSide.Frente, DeArchivo);

        await resolutor.ObtenerAsync(7, ImageSide.Frente);

        Assert.Equal(0, heredadas.Lecturas);
    }
}
