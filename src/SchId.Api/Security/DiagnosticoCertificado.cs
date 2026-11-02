using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SchId.Api.Security;

/// <summary>
/// Reporta en el log qué certificado va a usar Kestrel para HTTPS.
///
/// Existe por un caso concreto que costó encontrar: el kiosko fallaba al
/// instante con "connection closed" y en el servidor no aparecía nada. Un saludo
/// TLS que el servidor corta no deja rastro con el nivel de log por omisión, así
/// que desde afuera parecía un problema de la app cuando en realidad el
/// certificado no era utilizable.
///
/// La causa habitual es que el proceso **no puede leer la llave privada**: el
/// certificado vive en el almacén de la máquina y la cuenta con la que corre el
/// servicio no tiene permiso sobre la llave. El certificado carga —la parte
/// pública siempre se puede leer— pero el saludo TLS no se puede completar.
/// </summary>
public static class DiagnosticoCertificado
{
    public static void Reportar(IConfiguration configuracion, ILogger logger)
    {
        var seccion = configuracion.GetSection("Kestrel:Endpoints:Https:Certificate");
        if (!seccion.Exists())
        {
            return;
        }

        var ruta = seccion["Path"];
        if (!string.IsNullOrWhiteSpace(ruta))
        {
            RevisarArchivo(ruta, seccion["Password"], logger);
            return;
        }

        var subject = seccion["Subject"];
        if (string.IsNullOrWhiteSpace(subject))
        {
            return;
        }

        var nombreAlmacen = seccion["Store"] ?? "My";
        var ubicacion = Enum.TryParse<StoreLocation>(seccion["Location"], out var loc)
            ? loc
            : StoreLocation.CurrentUser;

        try
        {
            using var almacen = new X509Store(nombreAlmacen, ubicacion);
            almacen.Open(OpenFlags.ReadOnly);

            var encontrados = almacen.Certificates
                .Find(X509FindType.FindBySubjectName, subject, validOnly: false)
                .OfType<X509Certificate2>()
                .OrderByDescending(c => c.NotAfter)
                .ToList();

            if (encontrados.Count == 0)
            {
                logger.LogError(
                    "HTTPS: no se encontró ningún certificado con Subject '{Subject}' en {Ubicacion}\\{Almacen}. " +
                    "Kestrel no podrá atender conexiones cifradas.",
                    subject, ubicacion, nombreAlmacen);
                return;
            }

            var certificado = encontrados[0];

            logger.LogInformation(
                "HTTPS: usando el certificado {Subject}, huella {Huella}, vence {Vence:yyyy-MM-dd}.",
                certificado.Subject, certificado.Thumbprint, certificado.NotAfter);

            var san = certificado.Extensions
                .FirstOrDefault(e => e.Oid?.Value == "2.5.29.17")
                ?.Format(false);

            logger.LogInformation("HTTPS: direcciones del certificado (SAN): {San}", san ?? "(ninguna)");

            ProbarLlavePrivada(certificado, logger);
            RevisarCadena(certificado, logger);

            if (certificado.NotAfter < DateTime.Now)
            {
                logger.LogError(
                    "HTTPS: el certificado venció el {Vence:yyyy-MM-dd}. Vuelve a correr New-SchIdCertificado.ps1.",
                    certificado.NotAfter);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HTTPS: no se pudo revisar el almacén de certificados.");
        }
    }

    /// <summary>
    /// Abre el .pfx igual que lo hará Kestrel, y explica el fallo si lo hay.
    ///
    /// Antes esto solo anunciaba la ruta y volvía. El resultado era que un .pfx
    /// que no se puede abrir no decía una palabra aquí y reventaba después en
    /// KestrelServerImpl.BindAsync con cuarenta líneas de stack y sin ninguna
    /// pista de qué hacer. Abrirlo aquí convierte eso en una línea.
    /// </summary>
    internal static void RevisarArchivo(string ruta, string? contrasena, ILogger logger)
    {
        var completa = Path.IsPathRooted(ruta) ? ruta : Path.Combine(AppContext.BaseDirectory, ruta);

        logger.LogInformation("HTTPS: certificado desde archivo {Ruta}.", completa);

        if (!File.Exists(completa))
        {
            logger.LogError("HTTPS: el archivo no existe. Kestrel no va a poder arrancar.");
            return;
        }

        try
        {
            using var certificado = new X509Certificate2(completa, contrasena);

            logger.LogInformation(
                "HTTPS: usando el certificado {Subject}, huella {Huella}, vence {Vence:yyyy-MM-dd}.",
                certificado.Subject, certificado.Thumbprint, certificado.NotAfter);

            var san = certificado.Extensions
                .FirstOrDefault(e => e.Oid?.Value == "2.5.29.17")
                ?.Format(false);

            logger.LogInformation("HTTPS: direcciones del certificado (SAN): {San}", san ?? "(ninguna)");

            ProbarLlavePrivada(certificado, logger);
            RevisarCadena(certificado, logger);

            if (certificado.NotAfter < DateTime.Now)
            {
                logger.LogError(
                    "HTTPS: el certificado venció el {Vence:yyyy-MM-dd}. Vuelve a correr New-SchIdCertificado.ps1.",
                    certificado.NotAfter);
            }
        }
        catch (CryptographicException ex)
        {
            logger.LogError("HTTPS: no se pudo abrir el .pfx. {Motivo}", ExplicarFalloDePfx(ex));
        }
    }

    /// <summary>
    /// Traduce el fallo al abrir un .pfx.
    ///
    /// Abrir un .pfx no es solo leer el archivo: Windows tiene que escribir la
    /// llave privada en un contenedor de claves, y ese paso puede fallar aunque
    /// el archivo se lea perfectamente. Por eso "Acceso denegado" aquí no
    /// significa que falten permisos sobre el .pfx.
    /// </summary>
    internal static string ExplicarFalloDePfx(CryptographicException ex)
    {
        var texto = ex.Message.ToLowerInvariant();

        if (texto.Contains("acceso denegado") || texto.Contains("access denied") ||
            texto.Contains("access is denied"))
        {
            return "Acceso denegado al abrir el archivo.\n" +
                "No son los permisos del .pfx: el archivo se leyó. Lo que falló es escribir la " +
                "llave privada en el contenedor de claves de la cuenta que corre el proceso.\n" +
                "Suele ser una de estas:\n" +
                "  - El proceso corre como servicio o con una cuenta sin perfil de usuario cargado.\n" +
                "  - El perfil existe pero su carpeta de claves no es escribible.\n" +
                "Lo que funciona siempre es no cargar el .pfx desde disco: impórtalo al almacén de " +
                "la máquina y configura Kestrel con Subject/Store/Location en vez de Path/Password.\n" +
                "  New-SchIdCertificado.ps1 -Instalar -ArchivoPfx ... -ArchivoCa ... -CuentaServicio \"<cuenta>\"\n" +
                $"Detalle: {ex.Message}";
        }

        if (texto.Contains("contraseña") || texto.Contains("password") || texto.Contains("red no es correcta"))
        {
            return "La contraseña no coincide con Kestrel:Endpoints:Https:Certificate:Password.\n" +
                $"Detalle: {ex.Message}";
        }

        return $"{ex.GetType().Name}: {ex.Message}";
    }

    /// <summary>
    /// Intenta firmar con la llave privada del certificado.
    ///
    /// No basta con mirar <c>HasPrivateKey</c>: esa propiedad dice que el
    /// certificado tiene una llave asociada, no que esta cuenta pueda abrirla.
    /// Cuando el certificado vive en el almacén de la máquina y el proceso corre
    /// con una cuenta sin permiso sobre la llave, <c>HasPrivateKey</c> devuelve
    /// true, el diagnóstico daba todo por bueno, y el saludo TLS fallaba de
    /// todos modos — que es exactamente el rastro que no dejaba ver el problema.
    ///
    /// Firmar unos bytes obliga a abrir el contenedor de la llave, que es lo
    /// mismo que hace el saludo TLS. Si esto pasa, TLS va a poder.
    /// </summary>
    internal static void ProbarLlavePrivada(X509Certificate2 certificado, ILogger logger)
    {
        const string comoArreglar =
            "Dale permiso de lectura sobre la llave a la cuenta con la que corre el servidor: " +
            "certlm.msc, botón derecho sobre el certificado, Todas las tareas, Administrar claves " +
            "privadas. O corre New-SchIdCertificado.ps1 con -CuentaServicio. Si lo estás " +
            "levantando en consola, prueba abriendo la consola como administrador.";

        if (!certificado.HasPrivateKey)
        {
            logger.LogError(
                "HTTPS: el certificado no trae llave privada. El saludo TLS va a fallar y el kiosko " +
                "solo verá que el servidor cerró la conexión. {ComoArreglar}",
                comoArreglar);
            return;
        }

        try
        {
            using var rsa = certificado.GetRSAPrivateKey();
            if (rsa is null)
            {
                logger.LogError(
                    "HTTPS: no se pudo obtener la llave privada RSA del certificado. {ComoArreglar}",
                    comoArreglar);
                return;
            }

            rsa.SignData(
                new byte[] { 1, 2, 3 },
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            logger.LogInformation(
                "HTTPS: la llave privada del certificado es utilizable por esta cuenta ({Cuenta}).",
                Environment.UserName);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(
                "HTTPS: el certificado tiene llave privada pero esta cuenta ({Cuenta}) NO puede usarla: {Motivo}. " +
                "Este es el caso que hace que el kiosko diga 'el servidor cerró la conexión durante el " +
                "saludo TLS' sin que aparezca ningún error aquí. {ComoArreglar}",
                Environment.UserName, ex.Message, comoArreglar);
        }
    }

    /// <summary>
    /// Revisa que la cadena del certificado sea válida para esta máquina.
    ///
    /// Importa porque Kestrel carga con <c>AllowInvalid: false</c>: si la CA no
    /// está en el almacén de raíces de confianza de este servidor, no es que
    /// cargue un certificado inválido, es que **no lo encuentra**. El error que
    /// se ve entonces habla de un certificado ausente, que manda a buscar en el
    /// lugar equivocado. Pasa al instalar el certificado compartido en una
    /// ubicación donde se olvidó importar la CA.
    /// </summary>
    internal static void RevisarCadena(X509Certificate2 certificado, ILogger logger)
    {
        using var cadena = new X509Chain();
        cadena.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        if (cadena.Build(certificado))
        {
            return;
        }

        var motivos = cadena.ChainStatus
            .Select(e => e.StatusInformation.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToArray();

        var noEsDeConfianza = cadena.ChainStatus
            .Any(e => e.Status is X509ChainStatusFlags.UntrustedRoot or X509ChainStatusFlags.PartialChain);

        if (noEsDeConfianza)
        {
            logger.LogError(
                "HTTPS: esta máquina no confía en la CA que firmó el certificado ({Motivos}). " +
                "Con AllowInvalid en false, Kestrel ni siquiera lo va a encontrar. Importa la CA: " +
                "New-SchIdCertificado.ps1 -Instalar -ArchivoPfx ... -ArchivoCa schid_ca.crt",
                string.Join("; ", motivos));
            return;
        }

        logger.LogWarning(
            "HTTPS: la cadena del certificado no valida limpiamente ({Motivos}).",
            string.Join("; ", motivos));
    }
}
