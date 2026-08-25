namespace SchId.Consulta.Nucleo;

/// <summary>
/// Cómo se alcanza el servidor de una ubicación.
///
/// Es el mismo planteamiento que usa la app Android, y por la misma razón: un
/// certificado TLS vale para los nombres que lleva en su SAN, así que atarlo a
/// la IP significaría un certificado por ubicación —y uno que caduca en cuanto
/// el router reparte otra dirección—.
///
/// La salida es no conectar nunca por IP. Se pide siempre
/// `https://[NombreCertificado]:puerto` y se resuelve ese nombre a la IP
/// configurada en el momento de abrir el socket (ver <see cref="ClienteSchId"/>).
/// Un solo certificado sirve para las 70 ubicaciones y una sola compilación
/// sirve para todas.
/// </summary>
public sealed record DestinoServidor(string Host, int Puerto, bool ConTls)
{
    /// <summary>El nombre que lleva el certificado, igual en todas las ubicaciones.</summary>
    public const string NombreCertificado = "schid-servidor";

    public const int PuertoPorOmision = 7443;

    /// <summary>
    /// Con TLS es siempre el nombre del certificado —la IP se aplica al
    /// resolver— y sin TLS se va directo al host, porque ahí no hay certificado
    /// que validar.
    /// </summary>
    public string UrlBase => ConTls
        ? $"https://{NombreCertificado}:{Puerto}"
        : $"http://{Host}:{Puerto}";

    /// <summary>
    /// Interpreta lo que se capturó en la configuración. Acepta
    /// `192.168.1.226`, `192.168.1.226:7443`, `servidor-hotel:7443` y
    /// `http://192.168.1.226:5080`.
    /// </summary>
    public static DestinoServidor? Interpretar(string? texto)
    {
        var resto = (texto ?? "").Trim();
        if (resto.Length == 0)
        {
            return null;
        }

        var conTls = true;
        if (resto.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            resto = resto["https://".Length..];
        }
        else if (resto.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            conTls = false;
            resto = resto["http://".Length..];
        }

        resto = resto.TrimEnd('/');
        if (resto.Length == 0)
        {
            return null;
        }

        var partes = resto.Split(':');
        if (partes.Length > 2)
        {
            return null;
        }

        var host = partes[0].Trim();
        if (host.Length == 0 || host.Any(char.IsWhiteSpace))
        {
            return null;
        }

        int puerto;
        if (partes.Length == 2)
        {
            if (!int.TryParse(partes[1].Trim(), out puerto) || puerto is < 1 or > 65535)
            {
                return null;
            }
        }
        else
        {
            puerto = conTls ? PuertoPorOmision : 80;
        }

        return new DestinoServidor(host, puerto, conTls);
    }
}
