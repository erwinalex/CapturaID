using Microsoft.Extensions.Configuration;
using SchId.Consulta.Nucleo;

namespace SchId.Consulta;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var configuracion = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var direccion = configuracion["Servidor:Direccion"];
            var destino = DestinoServidor.Interpretar(direccion);
            if (destino is null)
            {
                Fallar(
                    "Falta Servidor:Direccion en appsettings.json, o no se entiende.\n\n" +
                    "Escribe la IP del servidor con su puerto, por ejemplo 192.168.1.226:7443.");
                return;
            }

            var token = configuracion["Servidor:Token"];
            if (string.IsNullOrWhiteSpace(token))
            {
                Fallar(
                    "Falta Servidor:Token en appsettings.json.\n\n" +
                    "Tiene que ser un token con el rol Consulta, no el del kiosko.");
                return;
            }

            Application.Run(new VentanaPrincipal(destino, token));
        }
        catch (Exception ex)
        {
            Fallar($"No se pudo iniciar la aplicación.\n\n{ex.Message}");
        }
    }

    private static void Fallar(string mensaje) =>
        MessageBox.Show(mensaje, "SchId Consulta", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
