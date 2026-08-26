using Microsoft.EntityFrameworkCore;
using SchId.Api.Data;

namespace SchId.Api.Diagnostico;

/// <summary>
/// Al arrancar, el servicio hace una consulta contra la base de datos.
///
/// Existe por un fallo real en producción: el servidor arrancaba sin una sola
/// queja, atendía peticiones, y reventaba la primera vez que alguien buscaba
/// —con una excepción de 40 líneas en el log del kiosko— porque le faltaba una
/// DLL nativa del cliente de SQL Server. Un servicio que arranca "bien" y falla
/// al primer uso real manda a buscar el problema al sitio equivocado: parecía
/// culpa de la búsqueda cuando era del empaquetado.
///
/// Probar la base al arrancar convierte eso en una línea en el log, junto a las
/// de HTTPS, antes de que nadie toque nada.
/// </summary>
public static class DiagnosticoBaseDeDatos
{
    public static async Task ReportarAsync(
        IServiceProvider servicios,
        ILogger logger,
        CancellationToken cancelacion = default)
    {
        try
        {
            using var ambito = servicios.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<SchIdDbContext>();

            var personas = await db.Personas.CountAsync(cancelacion);

            logger.LogInformation(
                "Base de datos: conexión correcta, {Personas} persona(s) registradas.",
                personas);
        }
        catch (Exception ex)
        {
            logger.LogError("Base de datos: {Motivo}", Explicar(ex));
        }
    }

    /// <summary>
    /// Traduce el fallo a algo accionable. La distinción importa porque manda a
    /// revisar sitios opuestos: una DLL que falta se arregla volviendo a copiar
    /// el programa, y una conexión rechazada se arregla en SQL Server.
    /// </summary>
    internal static string Explicar(Exception ex)
    {
        // La excepción real viene envuelta por EF Core, así que se busca en toda
        // la cadena: mirar solo la de fuera diría "error al abrir la conexión" y
        // se perdería el motivo verdadero.
        for (var actual = ex; actual is not null; actual = actual.InnerException)
        {
            if (actual is DllNotFoundException falta)
            {
                return "falta una biblioteca nativa del cliente de SQL Server. La instalación está " +
                    "incompleta: vuelve a copiar el ejecutable completo, sin dejar archivos atrás. " +
                    "El .exe publicado las lleva dentro, así que basta con reemplazarlo.\n" +
                    falta.Message;
            }
        }

        return $"no se pudo consultar. Revisa ConnectionStrings:SchIdDatabase, que SQL Server esté " +
            $"arriba y que la cuenta del servicio tenga acceso.\n{ex.GetType().Name}: {ex.Message}";
    }
}
