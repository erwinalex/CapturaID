using System.Net;
using System.Net.Http.Json;
using SchId.Api.Data.Entities;
using SchId.Api.Models;
using SchId.Shared;

namespace SchId.Tests;

/// <summary>
/// Los endpoints que usa la aplicación de escritorio de consulta.
///
/// Van por la API y no contra SQL Server directo por una razón concreta: las
/// imágenes nuevas viven como archivo en el disco **del servidor**. Una
/// aplicación en otra PC no puede leer esa carpeta sin un recurso compartido de
/// Windows, y compartir una carpeta llena de imágenes de INE para que la lea
/// cada terminal es peor que servirlas por aquí.
/// </summary>
public class ConsultaEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public ConsultaEndpointTests(ApiFactory api)
    {
        _api = api;
        Sembrar();
    }

    private void Sembrar()
    {
        using var db = _api.CrearContexto();
        if (db.Personas.Any())
        {
            return;
        }

        db.Personas.AddRange(
            new Persona
            {
                ID = 100,
                // Con el relleno de nchar, tal como viene de SQL Server.
                Nombre = "MARIA LOPEZ HERNANDEZ       ",
                CURP = "LOHM850315MDFPRR03",
                Nacionalidad = "MEXICANA  ",
                Edad = 40,
                Direccion = "AV SIEMPRE VIVA 742   ",
                FechaAlta = 45730.0m
            },
            new Persona
            {
                ID = 200,
                Nombre = "JUAN PEREZ GARCIA",
                CURP = "PEGJ740115HDFRRN08",
                Nacionalidad = "MEXICANA"
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task Se_busca_por_nombre()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);

        var resultados = await cliente.GetFromJsonAsync<List<ResultadoBusquedaResponse>>(
            "/api/personas/buscar?q=lopez");

        Assert.Single(resultados!);
        Assert.Equal(100, resultados![0].Id);
    }

    [Fact]
    public async Task Se_busca_por_CURP_con_el_mismo_parametro()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);

        var resultados = await cliente.GetFromJsonAsync<List<ResultadoBusquedaResponse>>(
            "/api/personas/buscar?q=PEGJ740115");

        Assert.Single(resultados!);
        Assert.Equal(200, resultados![0].Id);
    }

    /// <summary>
    /// Las columnas son nchar de largo fijo: sin recortar, el nombre llega con
    /// una cola de espacios que descuadra la lista y el PDF.
    /// </summary>
    [Fact]
    public async Task El_relleno_de_nchar_no_sale_hacia_afuera()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);

        var resultados = await cliente.GetFromJsonAsync<List<ResultadoBusquedaResponse>>(
            "/api/personas/buscar?q=lopez");

        Assert.Equal("MARIA LOPEZ HERNANDEZ", resultados![0].Nombre);
        Assert.Equal("MEXICANA", resultados[0].Nacionalidad);
    }

    /// <summary>
    /// 45730 en TDateTime es el 2025-03-14. Con el CAST de SQL Server saldría
    /// dos días corrido, que es la trampa que documenta DelphiDateTime.
    /// </summary>
    [Fact]
    public async Task La_fecha_de_Delphi_llega_convertida()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);

        var ficha = await cliente.GetFromJsonAsync<PersonaResponse>("/api/personas/100");

        Assert.Equal(new DateTime(2025, 3, 14), ficha!.FechaAlta);
    }

    [Fact]
    public async Task Una_busqueda_vacia_se_rechaza()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);

        var respuesta = await cliente.GetAsync("/api/personas/buscar?q=   ");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_id_que_no_existe_devuelve_404()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);

        var respuesta = await cliente.GetAsync("/api/personas/999999");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    /// <summary>
    /// El kiosko no puede consultar a nadie: su token solo tiene Captura. Que la
    /// tableta del mostrador pudiera listar huéspedes sería justo lo que el
    /// diseño evita.
    /// </summary>
    [Fact]
    public async Task El_token_del_kiosko_no_puede_buscar()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenKiosko);

        var busqueda = await cliente.GetAsync("/api/personas/buscar?q=lopez");
        var ficha = await cliente.GetAsync("/api/personas/100");
        var imagen = await cliente.GetAsync("/api/personas/100/imagen/frente");

        Assert.Equal(HttpStatusCode.Forbidden, busqueda.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ficha.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, imagen.StatusCode);
    }

    [Fact]
    public async Task Sin_token_no_se_consulta_nada()
    {
        var cliente = _api.ClienteCon(null);

        var respuesta = await cliente.GetAsync("/api/personas/buscar?q=lopez");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_lado_que_no_existe_se_rechaza()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);

        var respuesta = await cliente.GetAsync("/api/personas/100/imagen/perfil");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Una_persona_sin_imagenes_devuelve_404_en_la_imagen()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);

        var respuesta = await cliente.GetAsync("/api/personas/200/imagen/frente");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    /// <summary>
    /// La imagen que dejó la app móvil se sirve desde el archivo, y la cabecera
    /// lo dice: durante la migración es la forma de ver quién ya está migrado
    /// sin entrar a la base a mano.
    /// </summary>
    [Fact]
    public async Task La_imagen_en_archivo_se_sirve_y_se_dice_de_donde_salio()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0x11, 0x22 };
        Directory.CreateDirectory(_api.CarpetaImagenes);
        await File.WriteAllBytesAsync(
            ImagePathHelper.GetFullPath(_api.CarpetaImagenes, 100, ImageSide.Frente), jpeg);

        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);
        var respuesta = await cliente.GetAsync("/api/personas/100/imagen/frente");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("image/jpeg", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Archivo", respuesta.Headers.GetValues("X-Origen-Imagen").Single());
        Assert.Equal(jpeg, await respuesta.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// La persona del sistema anterior: su imagen sigue en la columna y la API
    /// la sirve igual, sin que quien consulta tenga que saber dónde estaba.
    /// </summary>
    [Fact]
    public async Task La_imagen_en_la_columna_tambien_se_sirve()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0x33, 0x44 };
        _api.ImagenesHeredadas.Poner(200, ImageSide.Reverso, jpeg);

        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);
        var respuesta = await cliente.GetAsync("/api/personas/200/imagen/reverso");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("ColumnaDeBaseDeDatos", respuesta.Headers.GetValues("X-Origen-Imagen").Single());
        Assert.Equal(jpeg, await respuesta.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// El caso real: la imagen del sistema anterior venía con una cabecera del
    /// BLOB por delante, así que no empezaba por la firma del formato y el
    /// visor no la reconocía. El servidor la entrega ya sin la cabecera.
    /// </summary>
    [Fact]
    public async Task Una_imagen_con_cabecera_del_BLOB_se_sirve_sin_ella()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 7, 7, 7, 7 };
        var cabecera = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        _api.ImagenesHeredadas.Poner(200, ImageSide.Frente, cabecera.Concat(jpeg).ToArray());

        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);
        var respuesta = await cliente.GetAsync("/api/personas/200/imagen/frente");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(jpeg, await respuesta.Content.ReadAsByteArrayAsync());
        Assert.Equal("JPEG", respuesta.Headers.GetValues("X-Formato-Imagen").Single());
    }

    /// <summary>
    /// Anunciar image/jpeg para algo que no lo es hace que el visor se niegue a
    /// abrirlo sin decir por qué. El tipo declarado es el real.
    /// </summary>
    [Fact]
    public async Task El_tipo_declarado_corresponde_al_formato_real()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        _api.ImagenesHeredadas.Poner(200, ImageSide.Reverso, png);

        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);
        var respuesta = await cliente.GetAsync("/api/personas/200/imagen/reverso");

        Assert.Equal("image/png", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("PNG", respuesta.Headers.GetValues("X-Formato-Imagen").Single());
    }
}
