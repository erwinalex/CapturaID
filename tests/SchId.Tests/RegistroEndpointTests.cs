using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SchId.Api.Models;
using SchId.Shared;

namespace SchId.Tests;

public class RegistroEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public RegistroEndpointTests(ApiFactory api) => _api = api;

    private static byte[] JpegDePrueba(byte marca) => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, marca };

    private static MultipartFormDataContent Formulario(
        string curp,
        string? nombre = null,
        string? direccion = null,
        int? edad = null,
        byte[]? frente = null,
        byte[]? reverso = null)
    {
        var contenido = new MultipartFormDataContent { { new StringContent(curp), "Curp" } };

        if (nombre is not null) contenido.Add(new StringContent(nombre), "Nombre");
        if (direccion is not null) contenido.Add(new StringContent(direccion), "Direccion");
        if (edad is not null) contenido.Add(new StringContent(edad.Value.ToString()), "Edad");

        if (frente is not null)
        {
            contenido.Add(new ByteArrayContent(frente), "imagenFrente", "frente.jpg");
        }

        if (reverso is not null)
        {
            contenido.Add(new ByteArrayContent(reverso), "imagenReverso", "reverso.jpg");
        }

        return contenido;
    }

    private async Task<RegistroResponse> RegistrarAsync(MultipartFormDataContent formulario)
    {
        var respuesta = await _api.ClienteCon(ApiFactory.TokenKiosko)
            .PostAsync("/api/personas/registro", formulario);

        respuesta.EnsureSuccessStatusCode();

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<RegistroResponse>(cuerpo, Json)!;
    }

    // ---------- Autenticación y roles ----------

    [Fact]
    public async Task Sin_token_no_se_puede_registrar()
    {
        var respuesta = await _api.ClienteCon(null)
            .PostAsync("/api/personas/registro", Formulario("SITK900101HDFRPN01"));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Con_un_token_inventado_no_se_puede_registrar()
    {
        var respuesta = await _api.ClienteCon("no-es-un-token")
            .PostAsync("/api/personas/registro", Formulario("INVE900101HDFRPN01"));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    /// <summary>
    /// El punto de separar los roles: si alguien se lleva la tableta del kiosko y
    /// saca su token, no debe poder descargarse los datos de los huéspedes.
    /// </summary>
    [Fact]
    public async Task El_token_del_kiosko_no_puede_consultar_datos_de_huespedes()
    {
        var respuesta = await _api.ClienteCon(ApiFactory.TokenKiosko)
            .GetAsync("/api/personas/curp/SITK900101HDFRPN01");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_token_de_consulta_no_puede_registrar_capturas()
    {
        var respuesta = await _api.ClienteCon(ApiFactory.TokenConsulta)
            .PostAsync("/api/personas/registro", Formulario("CONS900101HDFRPN01"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_token_valido_sin_roles_no_puede_hacer_nada()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenSinRoles);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await cliente.PostAsync("/api/personas/registro", Formulario("ROLE900101HDFRPN01"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cliente.GetAsync("/api/personas/curp/ROLE900101HDFRPN01")).StatusCode);
    }

    // ---------- Alta, comparación y actualización ----------

    [Fact]
    public async Task Un_curp_nuevo_se_da_de_alta()
    {
        var resultado = await RegistrarAsync(
            Formulario("ALTA900101HDFRPN01", nombre: "PEDRO RAMIREZ", edad: 36));

        Assert.Equal(ResultadoRegistro.Creado, resultado.Resultado);
        Assert.True(resultado.Id > 0);
        Assert.Empty(resultado.CamposActualizados);
    }

    [Fact]
    public async Task Volver_a_capturar_la_misma_ine_no_duplica_ni_reporta_cambios()
    {
        var primera = await RegistrarAsync(
            Formulario("IGUA900101HDFRPN01", nombre: "LAURA SOTO", direccion: "CALLE 1", edad: 30));

        var segunda = await RegistrarAsync(
            Formulario("IGUA900101HDFRPN01", nombre: "LAURA SOTO", direccion: "CALLE 1", edad: 30));

        Assert.Equal(ResultadoRegistro.Creado, primera.Resultado);
        Assert.Equal(ResultadoRegistro.SinCambios, segunda.Resultado);
        Assert.Equal(primera.Id, segunda.Id);
        Assert.Empty(segunda.CamposActualizados);

        using var db = _api.CrearContexto();
        Assert.Equal(1, await db.Personas.CountAsync(p => p.CURP == "IGUA900101HDFRPN01"));
    }

    [Fact]
    public async Task Si_cambio_un_dato_de_la_ine_se_actualiza_el_registro_previo()
    {
        var primera = await RegistrarAsync(
            Formulario("CAMB900101HDFRPN01", nombre: "JOSE LUNA", direccion: "CALLE VIEJA 1", edad: 40));

        var segunda = await RegistrarAsync(
            Formulario("CAMB900101HDFRPN01", nombre: "JOSE LUNA", direccion: "AVENIDA NUEVA 99", edad: 41));

        Assert.Equal(ResultadoRegistro.Actualizado, segunda.Resultado);
        Assert.Equal(primera.Id, segunda.Id);
        Assert.Equal(new[] { "Direccion", "Edad" }, segunda.CamposActualizados);

        using var db = _api.CrearContexto();
        var persona = await db.Personas.SingleAsync(p => p.ID == segunda.Id);
        Assert.Equal("AVENIDA NUEVA 99", persona.Direccion);
        Assert.Equal(41, persona.Edad);
    }

    /// <summary>
    /// El kiosko está en un mostrador: la respuesta no debe llevar datos del
    /// huésped que la app pueda dejar en pantalla o en memoria.
    /// </summary>
    [Fact]
    public async Task La_respuesta_al_kiosko_no_incluye_datos_personales()
    {
        await RegistrarAsync(Formulario("PRIV900101HDFRPN01", nombre: "SECRETO APELLIDO", direccion: "CALLE PRIVADA 5"));

        var respuesta = await _api.ClienteCon(ApiFactory.TokenKiosko)
            .PostAsync("/api/personas/registro", Formulario("PRIV900101HDFRPN01", nombre: "SECRETO APELLIDO"));

        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        Assert.DoesNotContain("SECRETO", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CALLE PRIVADA", cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task El_curp_llega_normalizado_a_mayusculas()
    {
        var resultado = await RegistrarAsync(Formulario("  mayu900101hdfrpn01  ", nombre: "ANA GIL"));

        using var db = _api.CrearContexto();
        var persona = await db.Personas.SingleAsync(p => p.ID == resultado.Id);
        Assert.Equal("MAYU900101HDFRPN01", persona.CURP);
    }

    [Fact]
    public async Task Sin_curp_se_rechaza_la_captura()
    {
        var respuesta = await _api.ClienteCon(ApiFactory.TokenKiosko)
            .PostAsync("/api/personas/registro", Formulario("   "));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_alta_deja_puesta_la_fecha_en_formato_Delphi()
    {
        var resultado = await RegistrarAsync(Formulario("FECH900101HDFRPN01", nombre: "RAUL DIAZ"));

        using var db = _api.CrearContexto();
        var persona = await db.Personas.SingleAsync(p => p.ID == resultado.Id);

        var fechaAlta = DelphiDateTime.ToDateTime(persona.FechaAlta);
        Assert.NotNull(fechaAlta);
        Assert.True(Math.Abs((DateTime.Now - fechaAlta!.Value).TotalMinutes) < 5,
            $"FechaAlta quedó en {fechaAlta}, que no corresponde a la fecha actual.");
    }

    /// <summary>
    /// El resultado viaja como texto ("Creado") y no como número. Se comprueba
    /// sobre el JSON crudo a propósito: el deserializador de las otras pruebas
    /// acepta las dos formas, así que dejó pasar durante un tiempo que la API
    /// mandara el número — el cliente Android lee un string y el README
    /// documenta un string.
    /// </summary>
    [Fact]
    public async Task El_resultado_viaja_como_texto_no_como_numero()
    {
        var respuesta = await _api.ClienteCon(ApiFactory.TokenKiosko)
            .PostAsync("/api/personas/registro", Formulario("TEXT900101HDFRPN01", nombre: "RITA SOSA"));

        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        Assert.Contains("\"resultado\":\"Creado\"", cuerpo);
        Assert.DoesNotContain("\"resultado\":0", cuerpo);
    }

    // ---------- Estancias: ya no son asunto de esta API ----------

    /// <summary>
    /// Quien administra dbo.Estancias es el PMS. Si la API volviera a crearlas,
    /// se duplicarían las estancias de cada huésped.
    /// </summary>
    [Fact]
    public async Task Registrar_no_crea_estancias()
    {
        await RegistrarAsync(Formulario("ESTA900101HDFRPN01", nombre: "CARLOS VEGA"));

        using var db = _api.CrearContexto();
        Assert.Empty(await db.Estancias.ToListAsync());
    }

    [Fact]
    public async Task El_endpoint_de_checkout_ya_no_existe()
    {
        var respuesta = await _api.ClienteCon(ApiFactory.TokenKiosko)
            .PostAsync("/api/personas/1/checkout", new StringContent(""));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    // ---------- Imágenes ----------

    [Fact]
    public async Task Las_imagenes_se_guardan_en_disco_con_el_nombre_derivado_del_id()
    {
        var resultado = await RegistrarAsync(Formulario(
            "IMAG900101HDFRPN01",
            nombre: "SOFIA MENA",
            frente: JpegDePrueba(0x01),
            reverso: JpegDePrueba(0x02)));

        Assert.True(resultado.ImagenFrenteGuardada);
        Assert.True(resultado.ImagenReversoGuardada);

        Assert.True(File.Exists(Path.Combine(_api.CarpetaImagenes, $"{resultado.Id}_frente.jpg")));
        Assert.True(File.Exists(Path.Combine(_api.CarpetaImagenes, $"{resultado.Id}_reverso.jpg")));
    }

    [Fact]
    public async Task Una_captura_posterior_reemplaza_las_imagenes()
    {
        var primera = await RegistrarAsync(Formulario(
            "REEM900101HDFRPN01", nombre: "IVAN ROJAS", frente: JpegDePrueba(0xAA)));

        await RegistrarAsync(Formulario(
            "REEM900101HDFRPN01", nombre: "IVAN ROJAS", frente: JpegDePrueba(0xBB)));

        var guardado = await File.ReadAllBytesAsync(
            Path.Combine(_api.CarpetaImagenes, $"{primera.Id}_frente.jpg"));

        Assert.Equal(JpegDePrueba(0xBB), guardado);
    }

    [Fact]
    public async Task Se_rechaza_un_archivo_que_no_es_jpeg()
    {
        var respuesta = await _api.ClienteCon(ApiFactory.TokenKiosko).PostAsync(
            "/api/personas/registro",
            Formulario("NOJP900101HDFRPN01", nombre: "LUIS PAZ",
                frente: new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    /// <summary>
    /// Si la imagen no sirve se rechaza la petición completa, para no dejar a la
    /// persona dada de alta pero sin fotos.
    /// </summary>
    [Fact]
    public async Task Una_imagen_invalida_no_da_de_alta_a_la_persona()
    {
        const string curp = "RECH900101HDFRPN01";

        await _api.ClienteCon(ApiFactory.TokenKiosko).PostAsync(
            "/api/personas/registro",
            Formulario(curp, nombre: "MARTA SUR", frente: new byte[] { 0x00, 0x01, 0x02, 0x03 }));

        using var db = _api.CrearContexto();
        Assert.False(await db.Personas.AnyAsync(p => p.CURP == curp));
    }

    [Fact]
    public async Task Se_rechaza_una_imagen_mas_grande_que_el_maximo_configurado()
    {
        var enorme = new byte[2048]; // el máximo en pruebas está en 1024 bytes
        enorme[0] = 0xFF;
        enorme[1] = 0xD8;
        enorme[2] = 0xFF;

        var respuesta = await _api.ClienteCon(ApiFactory.TokenKiosko).PostAsync(
            "/api/personas/registro",
            Formulario("BIGF900101HDFRPN01", nombre: "OMAR RUIZ", frente: enorme));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Se_puede_registrar_sin_mandar_imagenes()
    {
        var resultado = await RegistrarAsync(Formulario("NOIM900101HDFRPN01", nombre: "ELENA CRUZ"));

        Assert.Equal(ResultadoRegistro.Creado, resultado.Resultado);
        Assert.False(resultado.ImagenFrenteGuardada);
        Assert.False(resultado.ImagenReversoGuardada);
    }

    // ---------- Consulta ----------

    [Fact]
    public async Task El_rol_de_consulta_recupera_a_la_persona_capturada()
    {
        var alta = await RegistrarAsync(Formulario(
            "CONQ900101HDFRPN01", nombre: "DIEGO NAVA", direccion: "CALLE SOL 8", edad: 33,
            frente: JpegDePrueba(0x07)));

        var persona = await _api.ClienteCon(ApiFactory.TokenConsulta)
            .GetFromJsonAsync<PersonaResponse>($"/api/personas/curp/CONQ900101HDFRPN01");

        Assert.NotNull(persona);
        Assert.Equal(alta.Id, persona!.Id);
        Assert.Equal("DIEGO NAVA", persona.Nombre);
        Assert.Equal("CALLE SOL 8", persona.Direccion);
        Assert.Equal(33, persona.Edad);
        Assert.True(persona.TieneImagenFrente);
        Assert.False(persona.TieneImagenReverso);
    }

    [Fact]
    public async Task Un_curp_que_no_existe_devuelve_404()
    {
        var respuesta = await _api.ClienteCon(ApiFactory.TokenConsulta)
            .GetAsync("/api/personas/curp/NADA900101HDFRPN01");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
