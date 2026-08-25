using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace SchId.Consulta.Nucleo;

/// <summary>
/// Arma la ficha en PDF: los datos capturados y las dos caras de la
/// identificación.
///
/// El PDF lleva datos personales, así que no se guarda solo en ningún lado: se
/// devuelven los bytes y quien llama decide dónde ponerlos. La aplicación de
/// escritorio los escribe en una carpeta temporal que limpia al cerrar.
/// </summary>
public sealed class GeneradorFicha
{
    private const double Margen = 40;
    private const double AnchoImagen = 250;
    private const double SangriaValor = 130;
    private const double AltoRenglon = 20;

    public byte[] Generar(FichaPersona ficha, DateTime? generadaEl = null)
    {
        ResolutorFuentes.Instalar();

        using var documento = new PdfDocument();
        documento.Info.Title = $"Ficha de identificación {ficha.Id}";
        documento.Info.Creator = "SchId Consulta";

        var titulo = new XFont(ResolutorFuentes.Familia, 16, XFontStyleEx.Bold);
        var etiqueta = new XFont(ResolutorFuentes.Familia, 9, XFontStyleEx.Bold);
        var valor = new XFont(ResolutorFuentes.Familia, 11);
        var pie = new XFont(ResolutorFuentes.Familia, 8);

        var hoja = new Hoja(documento);

        hoja.Lienzo.DrawString("Ficha de identificación", titulo, XBrushes.Black, new XPoint(Margen, hoja.Y));
        hoja.Y += 24;
        hoja.Lienzo.DrawLine(XPens.Gray, Margen, hoja.Y, hoja.Ancho - Margen, hoja.Y);
        hoja.Y += 18;

        foreach (var (nombre, contenido) in Campos(ficha))
        {
            DibujarCampo(hoja, nombre, contenido, etiqueta, valor);
        }

        hoja.Y += 10;
        DibujarImagen(hoja, "Frente", ficha.Frente, etiqueta, pie);
        DibujarImagen(hoja, "Reverso", ficha.Reverso, etiqueta, pie);

        hoja.Cerrar();

        var momento = generadaEl ?? DateTime.Now;
        foreach (var pagina in documento.Pages.OfType<PdfPage>())
        {
            using var lienzo = XGraphics.FromPdfPage(pagina);
            lienzo.DrawString(
                $"Generada el {momento:yyyy-MM-dd HH:mm}. Contiene datos personales: trátese como tal.",
                pie,
                XBrushes.Gray,
                new XPoint(Margen, pagina.Height.Point - 25));
        }

        using var memoria = new MemoryStream();
        documento.Save(memoria);
        return memoria.ToArray();
    }

    /// <summary>
    /// Un campo vacío se dibuja igual, con una raya. Que la ficha tenga siempre
    /// los mismos renglones deja ver de un vistazo qué falta por capturar; si
    /// los vacíos se omitieran, un domicilio ausente pasaría desapercibido.
    /// </summary>
    internal static IEnumerable<(string Etiqueta, string Valor)> Campos(FichaPersona ficha)
    {
        yield return ("ID", ficha.Id.ToString());
        yield return ("Nombre", Mostrar(ficha.Nombre));
        yield return ("CURP", Mostrar(ficha.Curp));
        yield return ("Nacionalidad", Mostrar(ficha.Nacionalidad));
        yield return ("Edad", ficha.Edad?.ToString() ?? "—");
        yield return ("Domicilio", Mostrar(ficha.Direccion));
        yield return ("Teléfono", Mostrar(ficha.Telefono));
        yield return ("Residencia", Mostrar(ficha.Residencia));
        yield return ("Origen", Mostrar(ficha.Origen));
        yield return ("Fecha de alta", Fecha(ficha.FechaAlta));
        yield return ("Última modificación", Fecha(ficha.UltimaModificacion));
    }

    private static string Mostrar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? "—" : texto.Trim();

    private static string Fecha(DateTime? valor) =>
        valor is null ? "—" : valor.Value.ToString("yyyy-MM-dd");

    private static void DibujarCampo(Hoja hoja, string nombre, string contenido, XFont etiqueta, XFont valor)
    {
        var anchoDisponible = hoja.Ancho - Margen - SangriaValor - Margen;
        var renglones = PartirEnRenglones(hoja.Lienzo, contenido, valor, anchoDisponible);

        hoja.AsegurarEspacio(AltoRenglon * renglones.Count);

        hoja.Lienzo.DrawString(nombre, etiqueta, XBrushes.Gray, new XPoint(Margen, hoja.Y));

        foreach (var renglon in renglones)
        {
            hoja.Lienzo.DrawString(renglon, valor, XBrushes.Black, new XPoint(Margen + SangriaValor, hoja.Y));
            hoja.Y += AltoRenglon;
        }
    }

    /// <summary>
    /// Parte el texto para que quepa a lo ancho.
    ///
    /// Sin esto, un domicilio largo —que en la INE son la mayoría: calle,
    /// número, colonia, delegación y estado— se sale de la hoja y se pierde la
    /// mitad al imprimir.
    /// </summary>
    internal static List<string> PartirEnRenglones(XGraphics lienzo, string texto, XFont fuente, double ancho)
    {
        var renglones = new List<string>();
        var actual = "";

        foreach (var palabra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var propuesta = actual.Length == 0 ? palabra : $"{actual} {palabra}";

            // La condición de longitud cero evita el ciclo infinito con una
            // palabra sola más ancha que la hoja: se deja que se salga, que es
            // mejor que no dibujarla.
            if (actual.Length == 0 || lienzo.MeasureString(propuesta, fuente).Width <= ancho)
            {
                actual = propuesta;
            }
            else
            {
                renglones.Add(actual);
                actual = palabra;
            }
        }

        if (actual.Length > 0)
        {
            renglones.Add(actual);
        }

        return renglones.Count > 0 ? renglones : new List<string> { "" };
    }

    private static void DibujarImagen(Hoja hoja, string cara, ImagenIdentificacion? imagen, XFont etiqueta, XFont pie)
    {
        if (imagen is null)
        {
            hoja.AsegurarEspacio(38);
            hoja.Lienzo.DrawString(cara, etiqueta, XBrushes.Gray, new XPoint(Margen, hoja.Y));
            hoja.Y += 14;
            hoja.Lienzo.DrawString("(sin imagen)", pie, XBrushes.Gray, new XPoint(Margen, hoja.Y));
            hoja.Y += 24;
            return;
        }

        XImage? mapa = null;
        try
        {
            mapa = XImage.FromStream(new MemoryStream(imagen.Contenido));
        }
        catch (Exception)
        {
            // Un JPEG corrupto en la base no debe impedir imprimir la ficha con
            // el resto de los datos, que es justamente lo que se necesita
            // cuando algo salió mal con la imagen.
        }

        var alto = mapa is null ? 18 : AnchoImagen * mapa.PixelHeight / mapa.PixelWidth;
        hoja.AsegurarEspacio(14 + alto + 6 + 24);

        hoja.Lienzo.DrawString(cara, etiqueta, XBrushes.Gray, new XPoint(Margen, hoja.Y));
        hoja.Y += 14;

        if (mapa is null)
        {
            hoja.Lienzo.DrawString("(la imagen no se pudo leer)", pie, XBrushes.Red, new XPoint(Margen, hoja.Y));
            hoja.Y += 18;
        }
        else
        {
            hoja.Lienzo.DrawImage(mapa, Margen, hoja.Y, AnchoImagen, alto);
            hoja.Y += alto + 6;
        }

        // De dónde salió: durante la transición es lo que permite ver si una
        // persona ya está migrada sin entrar a la base a mano.
        hoja.Lienzo.DrawString(imagen.Descripcion, pie, XBrushes.Gray, new XPoint(Margen, hoja.Y));
        hoja.Y += 24;
    }

    /// <summary>
    /// La hoja sobre la que se va dibujando, con el salto de página resuelto.
    ///
    /// Sin esto, una persona con domicilio largo y dos imágenes se sale por
    /// abajo de la única página y el reverso no aparece — un fallo que solo se
    /// nota con datos reales, no con los de prueba.
    /// </summary>
    private sealed class Hoja
    {
        private readonly PdfDocument _documento;

        public Hoja(PdfDocument documento)
        {
            _documento = documento;
            Pagina = documento.AddPage();
            Lienzo = XGraphics.FromPdfPage(Pagina);
            Y = Margen;
        }

        public PdfPage Pagina { get; private set; }
        public XGraphics Lienzo { get; private set; }
        public double Y { get; set; }

        public double Ancho => Pagina.Width.Point;

        /// <summary>Deja sitio para el pie de página.</summary>
        private double Fondo => Pagina.Height.Point - Margen - 20;

        public void AsegurarEspacio(double alto)
        {
            if (Y + alto <= Fondo)
            {
                return;
            }

            Lienzo.Dispose();
            Pagina = _documento.AddPage();
            Lienzo = XGraphics.FromPdfPage(Pagina);
            Y = Margen;
        }

        /// <summary>
        /// El lienzo se tiene que soltar antes de guardar: PdfSharp escribe el
        /// contenido de la página al liberarlo.
        /// </summary>
        public void Cerrar() => Lienzo.Dispose();
    }
}
