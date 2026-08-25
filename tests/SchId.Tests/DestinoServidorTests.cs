using SchId.Consulta.Nucleo;

namespace SchId.Tests;

/// <summary>
/// Mismo planteamiento que <c>DireccionServidor</c> en la app Android: nunca se
/// conecta por IP, siempre por el nombre del certificado. Es lo que permite un
/// solo certificado y una sola compilación para las 70 ubicaciones.
/// </summary>
public class DestinoServidorTests
{
    [Fact]
    public void Una_IP_sola_apunta_a_https_en_el_puerto_de_siempre()
    {
        var destino = DestinoServidor.Interpretar("192.168.1.226")!;

        Assert.Equal("192.168.1.226", destino.Host);
        Assert.Equal(DestinoServidor.PuertoPorOmision, destino.Puerto);
        Assert.True(destino.ConTls);
    }

    [Fact]
    public void Se_respeta_el_puerto_que_se_escriba()
    {
        var destino = DestinoServidor.Interpretar("192.168.1.226:9443")!;

        Assert.Equal(9443, destino.Puerto);
    }

    /// <summary>
    /// El corazón del asunto: con TLS se pide siempre el nombre del
    /// certificado, nunca la IP.
    /// </summary>
    [Fact]
    public void Con_TLS_la_URL_lleva_el_nombre_del_certificado_y_no_la_IP()
    {
        var destino = DestinoServidor.Interpretar("192.168.1.226")!;

        Assert.Equal($"https://{DestinoServidor.NombreCertificado}:7443", destino.UrlBase);
        Assert.DoesNotContain("192.168", destino.UrlBase);
    }

    [Fact]
    public void Dos_ubicaciones_con_IP_distinta_piden_la_misma_URL()
    {
        var una = DestinoServidor.Interpretar("192.168.1.226:7443")!;
        var otra = DestinoServidor.Interpretar("10.0.0.9:7443")!;

        Assert.Equal(una.UrlBase, otra.UrlBase);
    }

    /// <summary>Sin TLS no hay certificado que validar, así que se va derecho a la IP.</summary>
    [Fact]
    public void Sin_cifrar_la_URL_va_directa_al_host()
    {
        var destino = DestinoServidor.Interpretar("http://192.168.1.226:5080")!;

        Assert.False(destino.ConTls);
        Assert.Equal("http://192.168.1.226:5080", destino.UrlBase);
    }

    [Fact]
    public void El_prefijo_https_es_opcional_y_no_cambia_nada()
    {
        Assert.Equal(
            DestinoServidor.Interpretar("192.168.1.226:7443"),
            DestinoServidor.Interpretar("https://192.168.1.226:7443"));
    }

    [Fact]
    public void Los_espacios_y_la_diagonal_final_no_estorban()
    {
        var destino = DestinoServidor.Interpretar("  192.168.1.226:7443/  ")!;

        Assert.Equal("192.168.1.226", destino.Host);
        Assert.Equal(7443, destino.Puerto);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("ftp://192.168.1.226")]
    [InlineData("192.168.1.226:7443:80")]
    [InlineData("192.168.1.226:puerto")]
    [InlineData("192.168.1.226:0")]
    [InlineData("192.168.1.226:70000")]
    [InlineData("servidor del hotel:7443")]
    [InlineData(":7443")]
    public void Lo_que_no_se_entiende_se_rechaza(string? texto)
    {
        Assert.Null(DestinoServidor.Interpretar(texto));
    }
}
