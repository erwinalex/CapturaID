using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace SchId.Api.Security;

/// <summary>
/// El resultado de intentar un saludo TLS contra el propio servidor.
/// </summary>
internal sealed record ResultadoAutoPrueba
{
    public bool Completado { get; init; }
    public string? Sujeto { get; init; }
    public string? HuellaCertificado { get; init; }
    public string? EmisorSujeto { get; init; }
    public string? HuellaEmisor { get; init; }
    public bool CadenaDeConfianza { get; init; }
    public string? ErrorDeValidacion { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Al arrancar, el servidor abre una conexión TLS contra sí mismo.
///
/// Existe porque el síntoma "saludo TLS que no se completa" se ve idéntico
/// desde los dos lados —una conexión que se abre y se cierra sin peticiones— y
/// ninguno de los dos logs dice quién cortó. Con los dos lados señalándose,
/// diagnosticar era ir probando a ciegas.
///
/// Un saludo contra uno mismo parte el problema en dos y no deja lugar a duda:
///
/// - Si se completa, el certificado del servidor sirve y su llave privada es
///   usable. Lo que falla está en el kiosko, casi siempre su schid_ca.crt.
/// - Si no se completa, el problema es del servidor y aquí sale la excepción,
///   sin tener que subir Kestrel a Debug.
///
/// Además imprime la huella de la CA que firmó el certificado, que es el dato
/// con el que se compara contra el schid_ca.crt compilado en la app: cuando se
/// regenera el certificado de un lado y no del otro, ahí se ve.
/// </summary>
public static class AutoPruebaTls
{
    /// <summary>
    /// Se conecta y hace el saludo. No valida la cadena para poder reportar el
    /// certificado aunque no sea de confianza —el objetivo es diagnosticar, no
    /// proteger esta conexión, que va contra el propio proceso.
    /// </summary>
    internal static async Task<ResultadoAutoPrueba> ProbarAsync(
        string host,
        int puerto,
        string nombreEsperado,
        CancellationToken cancelacion = default)
    {
        X509Certificate2? certificado = null;
        X509Certificate2? emisor = null;
        var erroresDePolitica = SslPolicyErrors.None;

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, puerto, cancelacion);

            using var tls = new SslStream(
                tcp.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (_, cert, cadena, errores) =>
                {
                    erroresDePolitica = errores;

                    if (cert is not null)
                    {
                        certificado = new X509Certificate2(cert);
                    }

                    // El último eslabón de la cadena es la raíz. Si la máquina
                    // no confía en la CA, la cadena se queda en el propio
                    // certificado y no hay emisor que reportar.
                    var elementos = cadena?.ChainElements;
                    if (elementos is { Count: > 1 })
                    {
                        emisor = new X509Certificate2(elementos[^1].Certificate);
                    }

                    return true;
                });

            await tls.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = nombreEsperado },
                cancelacion);

            return new ResultadoAutoPrueba
            {
                Completado = true,
                Sujeto = certificado?.Subject,
                HuellaCertificado = certificado?.Thumbprint,
                EmisorSujeto = emisor?.Subject,
                HuellaEmisor = emisor?.Thumbprint,
                CadenaDeConfianza = erroresDePolitica == SslPolicyErrors.None,
                ErrorDeValidacion = erroresDePolitica == SslPolicyErrors.None
                    ? null
                    : erroresDePolitica.ToString()
            };
        }
        catch (Exception ex)
        {
            return new ResultadoAutoPrueba
            {
                Completado = false,
                Sujeto = certificado?.Subject,
                HuellaCertificado = certificado?.Thumbprint,
                Error = ex.Message
            };
        }
        finally
        {
            certificado?.Dispose();
            emisor?.Dispose();
        }
    }

    /// <summary>
    /// Corre la prueba y la reporta. No propaga excepciones: es un diagnóstico,
    /// y tumbar el servicio por él sería peor que el problema que ayuda a
    /// encontrar.
    /// </summary>
    public static async Task ReportarAsync(
        IConfiguration configuracion,
        ILogger logger,
        string nombreEsperado = "schid-servidor",
        CancellationToken cancelacion = default)
    {
        var url = configuracion["Kestrel:Endpoints:Https:Url"];
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        var puerto = uri.IsDefaultPort ? 443 : uri.Port;

        try
        {
            var resultado = await ProbarAsync("127.0.0.1", puerto, nombreEsperado, cancelacion);

            if (!resultado.Completado)
            {
                logger.LogError(
                    "HTTPS: el servidor NO pudo completar un saludo TLS consigo mismo en el puerto {Puerto}: {Error}. " +
                    "El problema está aquí, no en el kiosko. Revisa las líneas HTTPS de arriba: llave privada " +
                    "utilizable y CA en el almacén de raíces.",
                    puerto, resultado.Error);
                return;
            }

            logger.LogInformation(
                "HTTPS: saludo TLS contra sí mismo completado en el puerto {Puerto}. El certificado y su llave " +
                "privada sirven. Certificado {Sujeto}, huella {Huella}.",
                puerto, resultado.Sujeto, resultado.HuellaCertificado);

            if (resultado.HuellaEmisor is not null)
            {
                logger.LogInformation(
                    "HTTPS: lo firmó la CA {Emisor}, huella {HuellaEmisor}. Esta huella tiene que ser la misma " +
                    "del schid_ca.crt que se compiló en la app; si no coincide, el kiosko no va a confiar.",
                    resultado.EmisorSujeto, resultado.HuellaEmisor);
            }

            if (!resultado.CadenaDeConfianza)
            {
                logger.LogError(
                    "HTTPS: el saludo se completó, pero esta máquina no valida la cadena del certificado ({Errores}). " +
                    "Importa la CA en el almacén de raíces: New-SchIdCertificado.ps1 -Instalar.",
                    resultado.ErrorDeValidacion);
                return;
            }

            logger.LogInformation(
                "HTTPS: si el kiosko sigue fallando con este servidor en verde, lo que falla es la app: " +
                "casi siempre su schid_ca.crt no corresponde a la CA de arriba.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HTTPS: no se pudo hacer la autoprueba de TLS.");
        }
    }
}
