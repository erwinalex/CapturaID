using SchId.Api.Diagnostico;

namespace SchId.Tests;

/// <summary>
/// El arranque prueba la base de datos para que un fallo de instalación se vea
/// como una línea en el log y no como una excepción de cuarenta líneas la
/// primera vez que alguien busca en el mostrador.
/// </summary>
public class DiagnosticoBaseDeDatosTests
{
    /// <summary>
    /// El caso que ocurrió en producción: faltaba una DLL nativa del cliente de
    /// SQL Server. El remedio no tiene nada que ver con la base —es volver a
    /// copiar el programa— así que el mensaje tiene que decir eso y no mandar a
    /// revisar la cadena de conexión.
    /// </summary>
    [Fact]
    public void Una_DLL_nativa_que_falta_se_reporta_como_instalacion_incompleta()
    {
        var motivo = DiagnosticoBaseDeDatos.Explicar(
            new DllNotFoundException("Unable to load DLL 'Microsoft.Data.SqlClient.SNI.dll'"));

        Assert.Contains("instalación está", motivo);
        Assert.DoesNotContain("ConnectionStrings", motivo);
    }

    /// <summary>
    /// EF Core envuelve la excepción real. Mirar solo la de fuera diría "error
    /// al abrir la conexión" y se perdería el motivo verdadero — que fue
    /// exactamente lo que costó ver en producción.
    /// </summary>
    [Fact]
    public void Se_busca_la_causa_en_toda_la_cadena_de_excepciones()
    {
        var envuelta = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new Exception("error al abrir la conexión",
                new DllNotFoundException("Unable to load DLL 'Microsoft.Data.SqlClient.SNI.dll'")));

        Assert.Contains("instalación está", DiagnosticoBaseDeDatos.Explicar(envuelta));
    }

    /// <summary>
    /// Un fallo normal de conexión sí manda a revisar la base: son remedios
    /// opuestos y confundirlos cuesta una tarde.
    /// </summary>
    [Fact]
    public void Un_fallo_de_conexion_manda_a_revisar_la_base()
    {
        var motivo = DiagnosticoBaseDeDatos.Explicar(
            new TimeoutException("El servidor no respondió"));

        Assert.Contains("ConnectionStrings", motivo);
        Assert.DoesNotContain("instalación está", motivo);
    }
}
