using Microsoft.Extensions.Configuration;

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

            var cadena = configuracion.GetConnectionString("SchIdDatabase");
            if (string.IsNullOrWhiteSpace(cadena))
            {
                Fallar("Falta ConnectionStrings:SchIdDatabase en appsettings.json.");
                return;
            }

            // Si no está configurada, se sigue adelante: las personas capturadas
            // con el sistema anterior tienen la imagen en la base y su ficha
            // sale igual. Lo que no se puede es callarlo, porque entonces las
            // fichas de la app móvil saldrían sin imagen sin explicación.
            var carpetaImagenes = configuracion["ImageStorage:BasePath"] ?? "";

            Application.Run(new VentanaPrincipal(cadena, carpetaImagenes));
        }
        catch (Exception ex)
        {
            Fallar($"No se pudo iniciar la aplicación.\n\n{ex.Message}");
        }
    }

    private static void Fallar(string mensaje) =>
        MessageBox.Show(mensaje, "SchId Consulta", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
