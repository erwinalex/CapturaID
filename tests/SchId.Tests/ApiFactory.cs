using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SchId.Api.Data;
using SchId.Api.Services;
using SchId.Shared;

namespace SchId.Tests;

/// <summary>
/// Levanta la API completa en memoria (ruteo, autenticación, autorización,
/// binding de multipart) sustituyendo solo lo que no puede correr en pruebas:
/// SQL Server por el proveedor en memoria y la carpeta de imágenes por una
/// temporal. El job de retención se quita porque aquí no aporta y correría en
/// segundo plano durante las pruebas.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string TokenKiosko = "token-de-prueba-kiosko";
    public const string TokenConsulta = "token-de-prueba-consulta";
    public const string TokenSinRoles = "token-de-prueba-sin-roles";

    private readonly string _nombreBase = "schid-" + Guid.NewGuid().ToString("N");

    public string CarpetaImagenes { get; } = Path.Combine(
        Path.GetTempPath(), "schid-itests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImageStorage:BasePath"] = CarpetaImagenes,
                ["ImageStorage:MaxBytesPorImagen"] = "1024",

                ["Autenticacion:Tokens:0:Nombre"] = "kiosko-prueba",
                ["Autenticacion:Tokens:0:Token"] = TokenKiosko,
                ["Autenticacion:Tokens:0:Roles:0"] = "Captura",

                ["Autenticacion:Tokens:1:Nombre"] = "consulta-prueba",
                ["Autenticacion:Tokens:1:Token"] = TokenConsulta,
                ["Autenticacion:Tokens:1:Roles:0"] = "Consulta",

                ["Autenticacion:Tokens:2:Nombre"] = "sin-roles",
                ["Autenticacion:Tokens:2:Token"] = TokenSinRoles
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<SchIdDbContext>>();
            services.RemoveAll<SchIdDbContext>();
            services.AddDbContext<SchIdDbContext>(options => options.UseInMemoryDatabase(_nombreBase));

            services.RemoveAll<IHostedService>();

            // Las columnas [image] se leen con SQL crudo, que el proveedor en
            // memoria no soporta. Se sustituye por un doble que las pruebas
            // pueden sembrar.
            services.RemoveAll<IImagenesHeredadas>();
            services.AddSingleton<IImagenesHeredadas>(ImagenesHeredadas);
        });
    }

    /// <summary>Las imágenes "del sistema anterior" que ven las pruebas.</summary>
    public ImagenesHeredadasDePrueba ImagenesHeredadas { get; } = new();

    public sealed class ImagenesHeredadasDePrueba : IImagenesHeredadas
    {
        private readonly Dictionary<(long, ImageSide), byte[]> _contenido = new();

        public void Poner(long personaId, ImageSide lado, byte[] contenido) =>
            _contenido[(personaId, lado)] = contenido;

        public Task<byte[]?> LeerAsync(long personaId, ImageSide lado, CancellationToken ct = default) =>
            Task.FromResult(_contenido.GetValueOrDefault((personaId, lado)));
    }

    public HttpClient ClienteCon(string? token)
    {
        var cliente = CreateClient();
        if (token is not null)
        {
            cliente.DefaultRequestHeaders.Add("X-Api-Key", token);
        }

        return cliente;
    }

    public SchIdDbContext CrearContexto()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SchIdDbContext>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(CarpetaImagenes))
        {
            Directory.Delete(CarpetaImagenes, recursive: true);
        }
    }
}
