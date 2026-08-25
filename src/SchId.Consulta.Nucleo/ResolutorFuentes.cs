using System.Reflection;
using PdfSharp.Fonts;

namespace SchId.Consulta.Nucleo;

/// <summary>
/// Le da a PdfSharp la fuente con la que dibujar, desde dentro del ensamblado.
///
/// Sin esto, PdfSharp busca las fuentes instaladas en el sistema y falla en
/// cuanto no encuentra la que se le pidió. Aparte del problema evidente —fuera
/// de Windows no encuentra ninguna—, depender de lo que tenga instalado cada
/// máquina significa que la misma ficha se vería distinta según la ubicación, y
/// que un servidor recién formateado podría dejar de generar fichas por una
/// razón que nadie relacionaría con el PDF.
///
/// Liberation Sans está bajo SIL OFL 1.1, que permite incrustarla y
/// redistribuirla (ver Fuentes/LICENSE-LiberationSans.txt), y es métricamente
/// compatible con Arial, así que la ficha queda con el aspecto esperado.
/// </summary>
public sealed class ResolutorFuentes : IFontResolver
{
    public const string Familia = "Liberation Sans";

    private const string Regular = "SchId.Consulta.Nucleo.Fuentes.LiberationSans-Regular.ttf";
    private const string Negrita = "SchId.Consulta.Nucleo.Fuentes.LiberationSans-Bold.ttf";

    private static readonly object Candado = new();
    private static bool _instalado;

    /// <summary>
    /// Se instala una sola vez por proceso. GlobalFontSettings es estático en
    /// PdfSharp, así que asignarlo en cada ficha sería tirar y rehacer la caché
    /// de fuentes en cada PDF.
    /// </summary>
    public static void Instalar()
    {
        if (_instalado)
        {
            return;
        }

        lock (Candado)
        {
            if (_instalado)
            {
                return;
            }

            GlobalFontSettings.FontResolver = new ResolutorFuentes();
            _instalado = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Cualquier familia que se pida se sirve con la incrustada. La ficha usa
        // una sola familia; devolver null aquí haría que PdfSharp lanzara la
        // misma excepción que este resolutor existe para evitar.
        return new FontResolverInfo(isBold ? Negrita : Regular);
    }

    public byte[]? GetFont(string faceName)
    {
        using var flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream(faceName);
        if (flujo is null)
        {
            return null;
        }

        using var memoria = new MemoryStream();
        flujo.CopyTo(memoria);
        return memoria.ToArray();
    }
}
