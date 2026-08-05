using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using SchId.Api.Data;
using SchId.Api.Security;
using SchId.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Permite que este mismo ejecutable corra como Windows Service cuando se
// instala con `sc create` / NSSM, o como consola normal al correrlo con
// `dotnet run`. No requiere cambios entre ambos modos.
builder.Host.UseWindowsService();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(ApiKeyAuthenticationHandler.NombreEsquema, new OpenApiSecurityScheme
    {
        Name = ApiKeyOptions.NombreEncabezado,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Token del cliente. Se manda en el encabezado X-Api-Key."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = ApiKeyAuthenticationHandler.NombreEsquema
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<SchIdDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SchIdDatabase")));

builder.Services.Configure<ImageStorageOptions>(builder.Configuration.GetSection("ImageStorage"));
builder.Services.AddSingleton<IImageStorageService, ImageStorageService>();

builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection("Retention"));
builder.Services.AddHostedService<RetentionCleanupService>();

builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SeccionConfiguracion));

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.NombreEsquema)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.NombreEsquema, _ => { });

builder.Services.AddAuthorization(options =>
{
    // Política por omisión: todo endpoint exige token, incluso uno que se agregue
    // después y al que se le olvide poner [Authorize]. Con datos de INE de por
    // medio, el descuido debe fallar cerrado, no abierto.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

ValidarTokensConfigurados(app.Services, app.Logger);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Solo redirige a HTTPS si de verdad hay un endpoint HTTPS configurado. Como
// Windows Service no aplica launchSettings.json, activarlo a ciegas hacía que
// Kestrel respondiera 307 hacia un puerto donde no escucha nadie.
if (builder.Configuration.GetSection("Kestrel:Endpoints:Https").Exists())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Sin tokens configurados nadie puede entrar, y es mejor enterarse al arrancar
// el servicio que cuando el kiosko empiece a recibir 401 en el mostrador.
static void ValidarTokensConfigurados(IServiceProvider servicios, ILogger logger)
{
    var opciones = servicios
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiKeyOptions>>()
        .Value;

    var validos = opciones.Tokens.Where(t => !string.IsNullOrWhiteSpace(t.Token)).ToList();

    if (validos.Count == 0)
    {
        throw new InvalidOperationException(
            "No hay tokens configurados en la sección 'Autenticacion:Tokens'. La API no " +
            "arranca sin ellos, porque quedaría abierta a cualquiera en la red. Ver el " +
            "apartado \"Autenticación\" del README para generar uno.");
    }

    foreach (var token in validos.Where(t => t.Roles.Count == 0))
    {
        logger.LogWarning(
            "El token '{Nombre}' no tiene roles asignados, así que no podrá usar ningún endpoint.",
            token.Nombre);
    }

    logger.LogInformation("Autenticación lista con {Count} token(s) configurado(s).", validos.Count);
}

/// <summary>Visible para que las pruebas de integración puedan levantar la API con WebApplicationFactory.</summary>
public partial class Program;
