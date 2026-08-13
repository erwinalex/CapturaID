using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SchId.Api.Security;

namespace SchId.Tests;

/// <summary>
/// La autoprueba existe para responder una sola pregunta —¿quién cortó el
/// saludo TLS?— así que se verifica contra un servidor TLS de verdad sobre
/// loopback, no contra un doble. Un diagnóstico que miente es peor que no
/// tenerlo: manda a revisar el lado equivocado.
/// </summary>
public class AutoPruebaTlsTests
{
    private const string Nombre = "schid-servidor";

    private static X509Certificate2 CrearCertificadoServidor()
    {
        using var llave = RSA.Create(2048);
        var solicitud = new CertificateRequest(
            $"CN={Nombre}",
            llave,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(Nombre);
        solicitud.CertificateExtensions.Add(san.Build());

        using var publico = solicitud.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2));

        // En Windows, SslStream exige que la llave privada venga de un
        // contenedor persistible; el viaje por PFX es la forma portátil de
        // conseguirlo y funciona igual en Linux.
        return new X509Certificate2(publico.Export(X509ContentType.Pfx, "prueba"), "prueba");
    }

    /// <summary>Levanta un TLS que atiende exactamente una conexión.</summary>
    private static (TcpListener Escucha, Task Servidor) LevantarServidorTls(X509Certificate2 certificado)
    {
        var escucha = new TcpListener(IPAddress.Loopback, 0);
        escucha.Start();

        var servidor = Task.Run(async () =>
        {
            using var cliente = await escucha.AcceptTcpClientAsync();
            using var tls = new SslStream(cliente.GetStream(), leaveInnerStreamOpen: false);
            await tls.AuthenticateAsServerAsync(certificado);
        });

        return (escucha, servidor);
    }

    private static int PuertoDe(TcpListener escucha) =>
        ((IPEndPoint)escucha.LocalEndpoint).Port;

    [Fact]
    public async Task Un_saludo_que_se_completa_se_reporta_como_completado()
    {
        using var certificado = CrearCertificadoServidor();
        var (escucha, servidor) = LevantarServidorTls(certificado);

        try
        {
            var resultado = await AutoPruebaTls.ProbarAsync("127.0.0.1", PuertoDe(escucha), Nombre);

            Assert.True(resultado.Completado, resultado.Error);
            Assert.Contains(Nombre, resultado.Sujeto);
            Assert.Equal(certificado.Thumbprint, resultado.HuellaCertificado);
            await servidor;
        }
        finally
        {
            escucha.Stop();
        }
    }

    /// <summary>
    /// El certificado es autofirmado y no está en el almacén de raíces, que es
    /// el mismo estado que un servidor donde se olvidó importar la CA. El saludo
    /// se completa —la autoprueba no valida— pero la cadena no es de confianza,
    /// y eso hay que decirlo por separado.
    /// </summary>
    [Fact]
    public async Task Una_cadena_que_no_es_de_confianza_se_distingue_del_saludo()
    {
        using var certificado = CrearCertificadoServidor();
        var (escucha, servidor) = LevantarServidorTls(certificado);

        try
        {
            var resultado = await AutoPruebaTls.ProbarAsync("127.0.0.1", PuertoDe(escucha), Nombre);

            Assert.True(resultado.Completado);
            Assert.False(resultado.CadenaDeConfianza);
            Assert.NotNull(resultado.ErrorDeValidacion);
            await servidor;
        }
        finally
        {
            escucha.Stop();
        }
    }

    /// <summary>
    /// Si no hay nadie escuchando, la autoprueba tiene que decirlo en lugar de
    /// lanzar: corre en el arranque del servicio.
    /// </summary>
    [Fact]
    public async Task Si_no_hay_nadie_escuchando_se_reporta_el_error()
    {
        var escucha = new TcpListener(IPAddress.Loopback, 0);
        escucha.Start();
        var puerto = PuertoDe(escucha);
        escucha.Stop();

        var resultado = await AutoPruebaTls.ProbarAsync("127.0.0.1", puerto, Nombre);

        Assert.False(resultado.Completado);
        Assert.NotNull(resultado.Error);
    }

    /// <summary>
    /// Un puerto que responde pero no habla TLS —el endpoint HTTP configurado
    /// por error como si fuera el de HTTPS— no debe reportarse como éxito.
    /// </summary>
    [Fact]
    public async Task Un_puerto_que_no_habla_TLS_no_se_reporta_como_completado()
    {
        var escucha = new TcpListener(IPAddress.Loopback, 0);
        escucha.Start();

        var servidor = Task.Run(async () =>
        {
            using var cliente = await escucha.AcceptTcpClientAsync();
            var respuesta = "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
            await cliente.GetStream().WriteAsync(respuesta);
        });

        try
        {
            var resultado = await AutoPruebaTls.ProbarAsync("127.0.0.1", PuertoDe(escucha), Nombre);

            Assert.False(resultado.Completado);
        }
        finally
        {
            escucha.Stop();
        }
    }
}
