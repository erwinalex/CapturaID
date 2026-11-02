using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using SchId.Api.Security;

namespace SchId.Tests;

/// <summary>
/// El diagnóstico del certificado cuando viene de un archivo .pfx.
///
/// Antes solo anunciaba la ruta y volvía sin abrirlo, así que un .pfx que no se
/// puede cargar no decía nada aquí y el servicio moría después en
/// KestrelServerImpl.BindAsync con cuarenta líneas de stack.
/// </summary>
public class DiagnosticoCertificadoArchivoTests : IDisposable
{
    private sealed class LoggerDePrueba : ILogger
    {
        public List<(LogLevel Nivel, string Mensaje)> Registros { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Registros.Add((logLevel, formatter(state, exception)));

        public bool HuboError => Registros.Any(r => r.Nivel == LogLevel.Error);
        public string Todo => string.Join("\n", Registros.Select(r => r.Mensaje));
    }

    private readonly string _carpeta = Directory.CreateTempSubdirectory().FullName;

    private string CrearPfx(string contrasena)
    {
        using var llave = RSA.Create(2048);
        var solicitud = new CertificateRequest(
            "CN=schid-servidor", llave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("schid-servidor");
        solicitud.CertificateExtensions.Add(san.Build());

        using var certificado = solicitud.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));

        var ruta = Path.Combine(_carpeta, "schid_servidor.pfx");
        File.WriteAllBytes(ruta, certificado.Export(X509ContentType.Pfx, contrasena));
        return ruta;
    }

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    [Fact]
    public void Un_pfx_que_abre_bien_reporta_sujeto_y_SAN()
    {
        var logger = new LoggerDePrueba();

        DiagnosticoCertificado.RevisarArchivo(CrearPfx("clave"), "clave", logger);

        Assert.Contains("schid-servidor", logger.Todo);
        Assert.Contains("SAN", logger.Todo);
        Assert.Contains("utilizable", logger.Todo);
    }

    /// <summary>
    /// El fallo tiene que salir aquí, no cuarenta líneas más abajo en Kestrel.
    /// </summary>
    [Fact]
    public void Una_contrasena_equivocada_se_reporta_como_error()
    {
        var logger = new LoggerDePrueba();

        DiagnosticoCertificado.RevisarArchivo(CrearPfx("clave"), "otra", logger);

        Assert.True(logger.HuboError);
        Assert.Contains("no se pudo abrir el .pfx", logger.Todo);
    }

    [Fact]
    public void Un_archivo_que_no_existe_se_reporta_como_error()
    {
        var logger = new LoggerDePrueba();

        DiagnosticoCertificado.RevisarArchivo(Path.Combine(_carpeta, "no-esta.pfx"), "x", logger);

        Assert.True(logger.HuboError);
        Assert.Contains("no existe", logger.Todo);
    }

    /// <summary>
    /// "Acceso denegado" al abrir un .pfx no son los permisos del archivo —el
    /// archivo se leyó— sino el contenedor de claves de la cuenta. Decir lo
    /// contrario manda a revisar los ACL del .pfx, que están bien.
    /// </summary>
    [Fact]
    public void Acceso_denegado_no_se_explica_como_permisos_del_archivo()
    {
        var motivo = DiagnosticoCertificado.ExplicarFalloDePfx(
            new CryptographicException("Acceso denegado."));

        Assert.Contains("No son los permisos del .pfx", motivo);
        Assert.Contains("contenedor de claves", motivo);
        Assert.Contains("Subject/Store/Location", motivo);
    }

    [Fact]
    public void Una_contrasena_mal_se_explica_como_contrasena()
    {
        var motivo = DiagnosticoCertificado.ExplicarFalloDePfx(
            new CryptographicException("The specified network password is not correct."));

        Assert.Contains("contraseña", motivo);
        Assert.DoesNotContain("contenedor de claves", motivo);
    }
}
