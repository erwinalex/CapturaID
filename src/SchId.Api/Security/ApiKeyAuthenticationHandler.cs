using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SchId.Api.Security;

/// <summary>
/// Autenticación por token fijo en el encabezado X-Api-Key.
///
/// Se eligió esto en lugar de usuario/contraseña porque el cliente es un kiosko
/// desatendido: no hay nadie que capture credenciales al arrancar la app. El
/// token se provisiona una vez en el dispositivo y se revoca desde
/// appsettings.json si el equipo se pierde.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string NombreEsquema = "ApiKey";

    private readonly ApiKeyOptions _apiKeys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ApiKeyOptions> apiKeys)
        : base(options, logger, encoder)
    {
        _apiKeys = apiKeys.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyOptions.NombreEncabezado, out var valores))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presentado = valores.ToString();
        if (string.IsNullOrWhiteSpace(presentado))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var entrada = _apiKeys.Tokens.FirstOrDefault(t => TokenCoincide(t.Token, presentado));

        if (entrada is null)
        {
            // Nunca registrar el token presentado en el log: los logs del
            // servicio suelen ser más fáciles de leer que la configuración.
            Logger.LogWarning(
                "Token rechazado en {Ruta} desde {IP}.",
                Request.Path, Context.Connection.RemoteIpAddress);

            return Task.FromResult(AuthenticateResult.Fail("Token no válido."));
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, entrada.Nombre) };
        claims.AddRange(entrada.Roles.Select(rol => new Claim(ClaimTypes.Role, rol)));

        var identidad = new ClaimsIdentity(claims, NombreEsquema);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identidad), NombreEsquema);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Compara en tiempo constante. Con un == normal, el tiempo de respuesta
    /// depende de cuántos caracteres iniciales acertó quien lo intenta, y eso
    /// permite adivinar el token carácter por carácter.
    /// </summary>
    private static bool TokenCoincide(string configurado, string presentado)
    {
        if (string.IsNullOrEmpty(configurado))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(configurado),
            Encoding.UTF8.GetBytes(presentado));
    }
}
