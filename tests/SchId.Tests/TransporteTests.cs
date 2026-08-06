using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SchId.Api.Data;

namespace SchId.Tests;

/// <summary>
/// Fija el comportamiento de la redirección a HTTPS, que depende de si hay un
/// endpoint HTTPS configurado.
///
/// El certificado en sí y el handshake TLS no se prueban aquí: eso lo resuelve
/// Kestrel y depende del certificado que haya en la máquina. Lo que sí puede
/// romperse en una edición y por eso se fija es la decisión de redirigir o no.
/// </summary>
public class TransporteTests
{
    private class Fabrica : WebApplicationFactory<Program>
    {
        private readonly bool _conHttps;

        public Fabrica(bool conHttps) => _conHttps = conHttps;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var valores = new Dictionary<string, string?>
                {
                    ["ImageStorage:BasePath"] = Path.Combine(Path.GetTempPath(), "schid-transporte"),
                    ["Autenticacion:Tokens:0:Nombre"] = "kiosko",
                    ["Autenticacion:Tokens:0:Token"] = "token",
                    ["Autenticacion:Tokens:0:Roles:0"] = "Captura"
                };

                if (_conHttps)
                {
                    valores["Kestrel:Endpoints:Https:Url"] = "https://0.0.0.0:7443";
                }

                config.AddInMemoryCollection(valores);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<SchIdDbContext>>();
                services.RemoveAll<SchIdDbContext>();
                services.AddDbContext<SchIdDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
                services.RemoveAll<IHostedService>();
            });
        }
    }

    /// <summary>
    /// Con HTTPS configurado, una petición que llega por HTTP se redirige en vez
    /// de atenderse: el token no debe seguir aceptándose en claro.
    /// </summary>
    [Fact]
    public async Task Con_https_configurado_las_peticiones_http_se_redirigen()
    {
        using var fabrica = new Fabrica(conHttps: true);
        var cliente = fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var respuesta = await cliente.GetAsync("http://localhost/api/personas/curp/ABC");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, respuesta.StatusCode);
        Assert.StartsWith("https://", respuesta.Headers.Location!.ToString());
    }

    /// <summary>
    /// Sin endpoint HTTPS, redirigir mandaría al cliente a un puerto donde no
    /// escucha nadie. Se atiende la petición (y el arranque deja el warning).
    /// </summary>
    [Fact]
    public async Task Sin_https_configurado_no_se_redirige_a_un_puerto_muerto()
    {
        using var fabrica = new Fabrica(conHttps: false);
        var cliente = fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var respuesta = await cliente.GetAsync("http://localhost/api/personas/curp/ABC");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }
}
