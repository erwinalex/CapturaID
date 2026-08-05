namespace SchId.Api.Security;

/// <summary>
/// Tokens válidos para consumir la API, configurados en appsettings.json (o
/// mejor, en variables de entorno / dotnet user-secrets para no dejarlos en el
/// repositorio).
///
/// Es una lista y no un solo token a propósito: así se le puede dar uno distinto
/// a cada kiosko y revocar el de un equipo extraviado sin dejar fuera a los
/// demás.
/// </summary>
public class ApiKeyOptions
{
    public const string SeccionConfiguracion = "Autenticacion";

    /// <summary>Encabezado HTTP donde la app manda su token.</summary>
    public const string NombreEncabezado = "X-Api-Key";

    public List<ApiKeyEntry> Tokens { get; set; } = new();
}

public class ApiKeyEntry
{
    /// <summary>Identifica al portador en los logs (ej. "kiosko-recepcion"). No es secreto.</summary>
    public string Nombre { get; set; } = "";

    /// <summary>El secreto que manda el cliente en el encabezado X-Api-Key.</summary>
    public string Token { get; set; } = "";

    /// <summary>Ver <see cref="Roles"/>.</summary>
    public List<string> Roles { get; set; } = new();
}

/// <summary>
/// Los dos permisos que existen. Están separados por una razón concreta: el
/// kiosko captura pero nunca muestra datos de huéspedes en pantalla, así que su
/// token no tiene por qué poder leerlos. Si alguien se lleva la tableta y saca
/// el token, con Captura solo puede dar de alta registros — no puede descargarse
/// el padrón de huéspedes.
/// </summary>
public static class RolesApi
{
    /// <summary>Registrar capturas de INE. Es el rol del kiosko.</summary>
    public const string Captura = "Captura";

    /// <summary>Leer datos de personas. Para el PMS o herramientas de soporte, no para el kiosko.</summary>
    public const string Consulta = "Consulta";
}
