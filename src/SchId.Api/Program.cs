using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;
using SchId.Api.Data;
using SchId.Api.Security;
using SchId.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Permite que este mismo ejecutable corra como Windows Service cuando se
// instala con `sc create` / NSSM, o como consola normal al correrlo con
// `dotnet run`. No requiere cambios entre ambos modos.
builder.Host.UseWindowsService();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Los enums viajan como texto ("Creado") y no como número (0). El
        // número obliga al cliente a conocer el orden de declaración, así que
        // reordenar el enum —algo que se ve inofensivo— cambiaría en silencio
        // el significado de lo que ya está publicado.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
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

// UseHttpsRedirection necesita saber a qué puerto redirigir. Si no se le dice,
// intenta deducirlo de las direcciones donde el servidor quedó escuchando, y
// cuando no lo logra NO redirige: atiende la petición por HTTP en silencio, que
// es justo lo que no queremos con un token de por medio. El puerto se toma del
// mismo lugar donde se configura el endpoint, para no tener dos números que
// mantener sincronizados.
//
// Se lee de forma diferida (Configure<IConfiguration>) y no aquí mismo, porque
// las fuentes de configuración que se agregan al final —variables de entorno de
// la instalación, o lo que inyecten las pruebas— todavía no están cargadas en
// este punto.
builder.Services.AddOptions<HttpsRedirectionOptions>()
    .Configure<IConfiguration>((options, configuracion) =>
    {
        var puerto = LeerPuertoHttps(configuracion["Kestrel:Endpoints:Https:Url"]);
        if (puerto is not null)
        {
            options.HttpsPort = puerto.Value;
        }
    });

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

// Se consulta app.Configuration y no builder.Configuration por lo mismo que el
// puerto de arriba: aquí ya están cargadas todas las fuentes.
var hayHttps = app.Configuration.GetSection("Kestrel:Endpoints:Https").Exists();
var hayHttp = app.Configuration.GetSection("Kestrel:Endpoints:Http").Exists();
AvisarSobreTransporte(app.Logger, hayHttps, hayHttp);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Solo redirige a HTTPS si de verdad hay un endpoint HTTPS configurado. Como
// Windows Service no aplica launchSettings.json, activarlo a ciegas hacía que
// Kestrel respondiera 307 hacia un puerto donde no escucha nadie.
if (hayHttps)
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

// Saca el puerto de una URL como "https://0.0.0.0:7443". Devuelve null si no hay
// URL configurada o si no se puede interpretar; en ese caso se deja que el
// middleware intente deducirlo por su cuenta.
static int? LeerPuertoHttps(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
    {
        return null;
    }

    // Uri no acepta comodines como 0.0.0.0 o * en el host, pero para sacar el
    // puerto da igual con qué host se sustituyan.
    var normalizada = url.Replace("//*:", "//localhost:").Replace("//+:", "//localhost:");

    if (!Uri.TryCreate(normalizada, UriKind.Absolute, out var uri) || uri.Port <= 0)
    {
        return null;
    }

    return uri.Port;
}

// El token del kiosko va en un encabezado HTTP: sin TLS viaja en claro por la
// red y cualquiera que la escuche se lo queda. No se puede impedir arrancar en
// HTTP (hace falta para la instalación inicial y para diagnosticar), pero sí
// dejarlo dicho en el log de forma que no pase inadvertido.
static void AvisarSobreTransporte(ILogger logger, bool hayHttps, bool hayHttp)
{
    if (!hayHttps)
    {
        logger.LogWarning(
            "La API está sirviendo SOLO HTTP: el token de los kioskos viaja en claro por la red. " +
            "Ver el apartado \"HTTPS en la red local\" del README para configurar el certificado.");
        return;
    }

    if (hayHttp)
    {
        logger.LogWarning(
            "Hay HTTPS configurado, pero el endpoint HTTP sigue abierto. Un kiosko mal configurado " +
            "puede seguir mandando su token en claro sin que nadie lo note. Una vez que los kioskos " +
            "estén en HTTPS, quita la sección Kestrel:Endpoints:Http de appsettings.json.");
        return;
    }

    logger.LogInformation("La API está sirviendo solo HTTPS.");
}

/// <summary>Visible para que las pruebas de integración puedan levantar la API con WebApplicationFactory.</summary>
public partial class Program;
