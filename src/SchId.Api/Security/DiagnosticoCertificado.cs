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
            logger.LogInformation("HTTPS: certificado desde archivo {Ruta}.", ruta);
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

            if (!certificado.HasPrivateKey)
            {
                logger.LogError(
                    "HTTPS: el certificado NO tiene llave privada accesible para esta cuenta ({Cuenta}). " +
                    "El saludo TLS va a fallar y el cliente verá que el servidor cerró la conexión, sin más " +
                    "detalle. Dale permiso de lectura sobre la llave: certlm.msc, botón derecho sobre el " +
                    "certificado, Todas las tareas, Administrar claves privadas.",
                    Environment.UserName);
            }

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
}
