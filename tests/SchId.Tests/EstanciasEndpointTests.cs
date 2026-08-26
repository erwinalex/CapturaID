using System.Net;
using System.Net.Http.Json;
using SchId.Api.Data.Entities;
using SchId.Api.Models;
using SchId.Consulta.Nucleo;

// La entidad de la base y el modelo del cliente se llaman igual, cada una en su
// sitio. Aquí conviven las dos, así que se distinguen con alias.
using EntidadEstancia = SchId.Api.Data.Entities.Estancia;

namespace SchId.Tests;

/// <summary>
/// El panel de estancias de la aplicación de consulta.
///
/// La API solo las lee: dbo.Estancias la escribe el PMS y esta API nunca la
/// toca. Lo que se verifica aquí es el orden, el recorte y —lo importante— que
/// un 0 en las columnas de fecha no acabe mostrándose como 1899-12-30.
/// </summary>
public class EstanciasEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    // 45730 = 2025-03-14, 45731 = 2025-03-15, 45000 = 2023-03-14.
    private const decimal Marzo14De2025 = 45730.0m;
    private const decimal Marzo15De2025 = 45731.0m;
    private const decimal Marzo14De2023 = 45000.0m;

    public EstanciasEndpointTests(ApiFactory api)
    {
        _api = api;
        Sembrar();
    }

    private void Sembrar()
    {
        using var db = _api.CrearContexto();
        if (db.Estancias.Any())
        {
            return;
        }

        db.Personas.Add(new Persona { ID = 700, Nombre = "HUESPED CON ESTANCIAS" });
        db.Personas.Add(new Persona { ID = 701, Nombre = "HUESPED SIN ESTANCIAS" });

        db.Estancias.AddRange(
            // La más antigua, ya cerrada.
            new EntidadEstancia
            {
                IdEstancia = 10,
                IdPersona = 700,
                FIngreso = Marzo14De2023,
                FSalida = Marzo14De2023 + 2,
                Hospedado = 0,
                TipoAsignacion = "NORMAL    "
            },
            // La más reciente, todavía abierta: FSalida en cero, que es como
            // queda una estancia sin cerrar en estos datos.
            new EntidadEstancia
            {
                IdEstancia = 11,
                IdPersona = 700,
                FIngreso = Marzo15De2025,
                FSalida = 0m,
                Hospedado = 1,
                TipoAsignacion = "NORMAL"
            },
            new EntidadEstancia
            {
                IdEstancia = 12,
                IdPersona = 700,
                FIngreso = Marzo14De2025,
                FSalida = Marzo15De2025,
                Hospedado = 0
            },
            // De otra persona: no debe salir en la de 700.
            new EntidadEstancia { IdEstancia = 13, IdPersona = 999, FIngreso = Marzo14De2025 });

        db.SaveChanges();
    }

    private async Task<List<EstanciaResponse>> Pedir(long id)
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenConsulta);
        return (await cliente.GetFromJsonAsync<List<EstanciaResponse>>(
            $"/api/personas/{id}/estancias"))!;
    }

    [Fact]
    public async Task Se_traen_solo_las_de_esa_persona()
    {
        var estancias = await Pedir(700);

        Assert.Equal(3, estancias.Count);
        Assert.All(estancias, e => Assert.NotEqual(13, e.IdEstancia));
    }

    /// <summary>
    /// De la más reciente a la más antigua: lo último es lo que casi siempre se
    /// busca, y en un panel corto tiene que estar arriba.
    /// </summary>
    [Fact]
    public async Task Vienen_de_la_mas_reciente_a_la_mas_antigua()
    {
        var estancias = await Pedir(700);

        Assert.Equal(new[] { 11L, 12L, 10L }, estancias.Select(e => e.IdEstancia).ToArray());
    }

    /// <summary>
    /// El detalle que sí importa: un 0 en la columna numeric se convierte en la
    /// época de Delphi (1899-12-30). Eso no es una salida, es un campo que nunca
    /// se llenó, y mostrarlo pondría una fecha absurda en el panel.
    /// </summary>
    [Fact]
    public async Task Una_salida_en_cero_no_se_muestra_como_1899()
    {
        var estancias = await Pedir(700);

        var abierta = estancias.Single(e => e.IdEstancia == 11);

        Assert.Null(abierta.Salida);
        Assert.Equal(new DateTime(2025, 3, 15), abierta.Ingreso);
    }

    [Fact]
    public async Task Las_fechas_de_Delphi_llegan_convertidas()
    {
        var estancias = await Pedir(700);

        var cerrada = estancias.Single(e => e.IdEstancia == 12);

        Assert.Equal(new DateTime(2025, 3, 14), cerrada.Ingreso);
        Assert.Equal(new DateTime(2025, 3, 15), cerrada.Salida);
    }

    /// <summary>
    /// nchar de largo fijo: sin recortar, el tipo llega con cola de espacios.
    /// </summary>
    [Fact]
    public async Task El_relleno_de_nchar_no_sale_hacia_afuera()
    {
        var estancias = await Pedir(700);

        Assert.Equal("NORMAL", estancias.Single(e => e.IdEstancia == 10).TipoAsignacion);
    }

    [Fact]
    public async Task Una_persona_sin_estancias_devuelve_una_lista_vacia()
    {
        Assert.Empty(await Pedir(701));
    }

    [Fact]
    public async Task El_token_del_kiosko_no_puede_ver_estancias()
    {
        var cliente = _api.ClienteCon(ApiFactory.TokenKiosko);

        var respuesta = await cliente.GetAsync("/api/personas/700/estancias");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_cliente_de_escritorio_las_lee_igual()
    {
        using var cliente = new ClienteSchId(
            new DestinoServidor("127.0.0.1", 7443, ConTls: true),
            _api.ClienteCon(ApiFactory.TokenConsulta));

        var estancias = await cliente.EstanciasAsync(700);

        Assert.Equal(3, estancias.Count);
        Assert.Equal(11, estancias[0].IdEstancia);
        Assert.Null(estancias[0].Salida);
        Assert.Equal(1, estancias[0].Hospedado);
    }
}
