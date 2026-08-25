using SchId.Consulta.Nucleo;
using SchId.Shared;

namespace SchId.Tests;

/// <summary>
/// El requisito central de la aplicación de consulta: hay personas capturadas
/// con la app móvil —imagen en archivo— y personas del sistema anterior —imagen
/// en la columna—, y la ficha tiene que salir bien en los dos casos.
/// </summary>
public class ResolutorImagenesTests
{
    private sealed class FuenteFalsa : IFuenteDeImagenes
    {
        private readonly Dictionary<(long, ImageSide), byte[]?> _contenido = new();

        public int Lecturas { get; private set; }

        public FuenteFalsa Con(long id, ImageSide lado, byte[]? bytes)
        {
            _contenido[(id, lado)] = bytes;
            return this;
        }

        public Task<byte[]?> LeerAsync(long personaId, ImageSide lado, CancellationToken cancelacion = default)
        {
            Lecturas++;
            return Task.FromResult(_contenido.GetValueOrDefault((personaId, lado)));
        }
    }

    private static readonly byte[] DeArchivo = { 1, 2, 3 };
    private static readonly byte[] DeBase = { 9, 9, 9 };

    [Fact]
    public async Task Se_lee_del_archivo_cuando_lo_dejo_la_app_movil()
    {
        var resolutor = new ResolutorImagenes(
            new FuenteFalsa().Con(7, ImageSide.Frente, DeArchivo),
            new FuenteFalsa());

        var imagen = await resolutor.ObtenerAsync(7, ImageSide.Frente);

        Assert.Equal(OrigenImagen.Archivo, imagen!.Origen);
        Assert.Equal(DeArchivo, imagen.Contenido);
    }

    [Fact]
    public async Task Se_cae_a_la_columna_cuando_la_persona_es_del_sistema_anterior()
    {
        var resolutor = new ResolutorImagenes(
            new FuenteFalsa(),
            new FuenteFalsa().Con(7, ImageSide.Reverso, DeBase));

        var imagen = await resolutor.ObtenerAsync(7, ImageSide.Reverso);

        Assert.Equal(OrigenImagen.ColumnaDeBaseDeDatos, imagen!.Origen);
        Assert.Equal(DeBase, imagen.Contenido);
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
        var resolutor = new ResolutorImagenes(
            new FuenteFalsa().Con(7, ImageSide.Frente, DeArchivo),
            new FuenteFalsa().Con(7, ImageSide.Frente, DeBase));

        var imagen = await resolutor.ObtenerAsync(7, ImageSide.Frente);

        Assert.Equal(DeArchivo, imagen!.Contenido);
    }

    /// <summary>
    /// Un archivo de cero bytes es una escritura que salió mal, no una imagen.
    /// Mejor enseñar la de la columna que una ficha sin identificación.
    /// </summary>
    [Fact]
    public async Task Un_archivo_vacio_no_cuenta_como_imagen()
    {
        var resolutor = new ResolutorImagenes(
            new FuenteFalsa().Con(7, ImageSide.Frente, Array.Empty<byte>()),
            new FuenteFalsa().Con(7, ImageSide.Frente, DeBase));

        var imagen = await resolutor.ObtenerAsync(7, ImageSide.Frente);

        Assert.Equal(OrigenImagen.ColumnaDeBaseDeDatos, imagen!.Origen);
    }

    [Fact]
    public async Task Sin_imagen_en_ningun_lado_no_se_inventa_nada()
    {
        var resolutor = new ResolutorImagenes(new FuenteFalsa(), new FuenteFalsa());

        Assert.Null(await resolutor.ObtenerAsync(7, ImageSide.Frente));
    }

    /// <summary>
    /// Ir a la base cuando el archivo ya respondió sería un viaje de red por
    /// cada cara de cada ficha, contra una columna [image] que pesa megabytes.
    /// </summary>
    [Fact]
    public async Task No_se_consulta_la_base_si_el_archivo_ya_respondio()
    {
        var baseDeDatos = new FuenteFalsa().Con(7, ImageSide.Frente, DeBase);
        var resolutor = new ResolutorImagenes(
            new FuenteFalsa().Con(7, ImageSide.Frente, DeArchivo),
            baseDeDatos);

        await resolutor.ObtenerAsync(7, ImageSide.Frente);

        Assert.Equal(0, baseDeDatos.Lecturas);
    }

    [Fact]
    public async Task El_archivo_se_busca_con_el_mismo_nombre_que_escribe_la_migracion()
    {
        var carpeta = Directory.CreateTempSubdirectory();
        try
        {
            var ruta = ImagePathHelper.GetFullPath(carpeta.FullName, 42, ImageSide.Reverso);
            await File.WriteAllBytesAsync(ruta, DeArchivo);

            var fuente = new ImagenesEnArchivo(carpeta.FullName);

            Assert.Equal(DeArchivo, await fuente.LeerAsync(42, ImageSide.Reverso));
            Assert.Null(await fuente.LeerAsync(42, ImageSide.Frente));
        }
        finally
        {
            carpeta.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Una_carpeta_que_no_existe_no_revienta()
    {
        var fuente = new ImagenesEnArchivo("/carpeta/que/no/existe");

        Assert.Null(await fuente.LeerAsync(1, ImageSide.Frente));
    }
}
