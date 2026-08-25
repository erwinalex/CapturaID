using SchId.Api.Data.Entities;
using SchId.Consulta.Nucleo;
using SchId.Shared;

namespace SchId.Tests;

/// <summary>
/// El cliente de la aplicación de escritorio contra la API de verdad, levantada
/// en memoria. No es un doble: se ejercitan las rutas, las cabeceras y los
/// códigos de estado reales, que es donde se rompen las cosas cuando el
/// servidor cambia y el cliente no se entera.
/// </summary>
public class ClienteSchIdTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    private static readonly DestinoServidor Destino = new("127.0.0.1", 7443, ConTls: true);

    public ClienteSchIdTests(ApiFactory api)
    {
        _api = api;
        Sembrar();
    }

    private void Sembrar()
    {
        using var db = _api.CrearContexto();
        if (db.Personas.Any(p => p.ID == 500))
        {
            return;
        }

        db.Personas.Add(new Persona
        {
            ID = 500,
            Nombre = "ROSA MARTINEZ SOTO        ",
            CURP = "MASR900620MDFRTS01",
            Nacionalidad = "MEXICANA",
            Direccion = "CALLE 5 DE MAYO 10   ",
            Edad = 35,
            FechaAlta = 45730.0m
        });

        // Aparte, y sin imágenes nunca: las pruebas comparten la fábrica, así
        // que si se reutilizara la 500 —a la que otra prueba le escribe un
        // archivo— el resultado dependería del orden de ejecución.
        db.Personas.Add(new Persona
        {
            ID = 501,
            Nombre = "PERSONA SIN IMAGENES",
            CURP = "SIIM900620MDFRTS02"
        });

        db.SaveChanges();
    }

    private ClienteSchId Cliente(string? token = ApiFactory.TokenConsulta) =>
        new(Destino, _api.ClienteCon(token));

    [Fact]
    public async Task Busca_y_devuelve_los_datos_recortados()
    {
        using var cliente = Cliente();

        var resultados = await cliente.BuscarAsync("martinez");

        Assert.Single(resultados);
        Assert.Equal("ROSA MARTINEZ SOTO", resultados[0].Nombre);
        Assert.Equal(new DateTime(2025, 3, 14), resultados[0].FechaAlta);
    }

    [Fact]
    public async Task Una_busqueda_vacia_ni_llega_al_servidor()
    {
        using var cliente = Cliente();

        Assert.Empty(await cliente.BuscarAsync("   "));
    }

    /// <summary>
    /// Un apellido con acento o un espacio tienen que viajar bien en la URL.
    /// </summary>
    [Fact]
    public async Task El_termino_se_codifica_para_la_URL()
    {
        using var cliente = Cliente();

        var resultados = await cliente.BuscarAsync("MARTINEZ SOTO");

        Assert.Single(resultados);
    }

    [Fact]
    public async Task La_ficha_trae_los_datos_completos()
    {
        using var cliente = Cliente();

        var ficha = await cliente.ObtenerFichaAsync(500);

        Assert.Equal("ROSA MARTINEZ SOTO", ficha!.Nombre);
        Assert.Equal("CALLE 5 DE MAYO 10", ficha.Direccion);
        Assert.Equal(new DateTime(2025, 3, 14), ficha.FechaAlta);
    }

    [Fact]
    public async Task Una_persona_que_no_existe_devuelve_nulo_y_no_revienta()
    {
        using var cliente = Cliente();

        Assert.Null(await cliente.ObtenerFichaAsync(999999));
    }

    /// <summary>
    /// Que una persona no tenga imágenes es normal, no un error: la ficha sale
    /// igual con el resto de los datos.
    /// </summary>
    [Fact]
    public async Task Sin_imagenes_la_ficha_sale_igual()
    {
        using var cliente = Cliente();

        var ficha = await cliente.ObtenerFichaAsync(501);

        Assert.Equal("PERSONA SIN IMAGENES", ficha!.Nombre);
        Assert.Null(ficha.Frente);
        Assert.Null(ficha.Reverso);
    }

    /// <summary>
    /// Las dos procedencias, extremo a extremo: la imagen que dejó la app móvil
    /// como archivo y la que sigue en la columna del sistema anterior. El
    /// cliente tiene que distinguirlas para poder decirlo en la ficha.
    /// </summary>
    [Fact]
    public async Task Se_distingue_la_imagen_de_archivo_de_la_de_la_columna()
    {
        var deArchivo = new byte[] { 0xFF, 0xD8, 0xFF, 0xAA };
        var deColumna = new byte[] { 0xFF, 0xD8, 0xFF, 0xBB };

        Directory.CreateDirectory(_api.CarpetaImagenes);
        await File.WriteAllBytesAsync(
            ImagePathHelper.GetFullPath(_api.CarpetaImagenes, 500, ImageSide.Frente), deArchivo);
        _api.ImagenesHeredadas.Poner(500, ImageSide.Reverso, deColumna);

        using var cliente = Cliente();
        var ficha = await cliente.ObtenerFichaAsync(500);

        Assert.Equal(OrigenImagen.Archivo, ficha!.Frente!.Origen);
        Assert.Equal(deArchivo, ficha.Frente.Contenido);

        Assert.Equal(OrigenImagen.ColumnaDeBaseDeDatos, ficha.Reverso!.Origen);
        Assert.Equal(deColumna, ficha.Reverso.Contenido);
    }

    /// <summary>
    /// Con el token del kiosko, el mensaje tiene que decir qué token hace falta.
    /// "403 Forbidden" no le dice nada a quien instala la aplicación.
    /// </summary>
    [Fact]
    public async Task Con_el_token_equivocado_el_mensaje_explica_cual_hace_falta()
    {
        using var cliente = Cliente(ApiFactory.TokenKiosko);

        var error = await Assert.ThrowsAsync<ErrorDeConsulta>(() => cliente.BuscarAsync("martinez"));

        Assert.Contains("Consulta", error.Message);
    }

    [Fact]
    public async Task Sin_token_el_mensaje_manda_a_revisar_la_configuracion()
    {
        using var cliente = Cliente(token: null);

        var error = await Assert.ThrowsAsync<ErrorDeConsulta>(() => cliente.BuscarAsync("martinez"));

        Assert.Contains("appsettings.json", error.Message);
    }

    /// <summary>
    /// La ficha completa, extremo a extremo: del servidor al PDF. Es el camino
    /// que recorre cada consulta real.
    /// </summary>
    [Fact]
    public async Task De_la_API_al_PDF()
    {
        Directory.CreateDirectory(_api.CarpetaImagenes);
        await File.WriteAllBytesAsync(
            ImagePathHelper.GetFullPath(_api.CarpetaImagenes, 500, ImageSide.Frente),
            new byte[] { 0xFF, 0xD8, 0xFF, 0xAA });

        using var cliente = Cliente();
        var ficha = await cliente.ObtenerFichaAsync(500);

        var pdf = new GeneradorFicha().Generar(ficha!);

        Assert.Equal((byte)'%', pdf[0]);
        Assert.True(pdf.Length > 500);
    }
}
