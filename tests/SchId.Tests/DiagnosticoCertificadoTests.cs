using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using SchId.Api.Security;

namespace SchId.Tests;

/// <summary>
/// El diagnóstico existe para un fallo que no deja rastro: el saludo TLS que el
/// servidor corta. Si el diagnóstico se equivoca, manda a buscar al lugar
/// contrario — que fue justo lo que pasó cuando solo miraba HasPrivateKey.
/// </summary>
public class DiagnosticoCertificadoTests
{
    /// <summary>Recoge lo que se registró, para poder afirmar sobre el nivel.</summary>
    private sealed class LoggerDePrueba : ILogger
    {
        public List<(LogLevel Nivel, string Mensaje)> Registros { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Registros.Add((logLevel, formatter(state, exception)));
        }

        public bool HuboError => Registros.Any(r => r.Nivel == LogLevel.Error);

        public string Todo => string.Join("\n", Registros.Select(r => r.Mensaje));
    }

    private static X509Certificate2 CrearCertificado()
    {
        using var llave = RSA.Create(2048);
        var solicitud = new CertificateRequest(
            "CN=schid-servidor",
            llave,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return solicitud.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2));
    }

    [Fact]
    public void Una_llave_privada_utilizable_no_reporta_error()
    {
        using var certificado = CrearCertificado();
        var logger = new LoggerDePrueba();

        DiagnosticoCertificado.ProbarLlavePrivada(certificado, logger);

        Assert.False(logger.HuboError);
        Assert.Contains("utilizable", logger.Todo);
    }

    /// <summary>
    /// Exportar solo la parte pública deja un certificado con HasPrivateKey en
    /// false: es el caso que el diagnóstico ya detectaba.
    /// </summary>
    [Fact]
    public void Un_certificado_sin_llave_privada_se_reporta_como_error()
    {
        using var completo = CrearCertificado();
        using var soloPublico = new X509Certificate2(completo.Export(X509ContentType.Cert));
        var logger = new LoggerDePrueba();

        DiagnosticoCertificado.ProbarLlavePrivada(soloPublico, logger);

        Assert.True(logger.HuboError);
    }

    /// <summary>
    /// Lo que se le escapaba al diagnóstico anterior: mirar HasPrivateKey no
    /// prueba nada, porque puede ser true y la cuenta no poder abrir la llave.
    /// La prueba de verdad es firmar.
    /// </summary>
    [Fact]
    public void Se_intenta_firmar_y_no_solo_mirar_la_bandera()
    {
        using var certificado = CrearCertificado();
        var logger = new LoggerDePrueba();

        Assert.True(certificado.HasPrivateKey);

        DiagnosticoCertificado.ProbarLlavePrivada(certificado, logger);

        // Si solo se mirara la bandera, no habría nada que decir sobre la cuenta.
        Assert.Contains(Environment.UserName, logger.Todo);
    }

    /// <summary>
    /// Un autofirmado que no está en el almacén de raíces no encadena, que es
    /// exactamente el estado de un servidor donde se instaló el .pfx pero se
    /// olvidó importar la CA.
    /// </summary>
    [Fact]
    public void Una_cadena_que_no_llega_a_una_raiz_de_confianza_se_reporta()
    {
        using var certificado = CrearCertificado();
        var logger = new LoggerDePrueba();

        DiagnosticoCertificado.RevisarCadena(certificado, logger);

        Assert.True(logger.HuboError);
        Assert.Contains("-Instalar", logger.Todo);
    }
}
