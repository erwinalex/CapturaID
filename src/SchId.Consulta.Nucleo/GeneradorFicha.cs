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

        DibujarEstancias(hoja, ficha.Estancias, etiqueta, valor, pie);

        hoja.Cerrar();

        var momento = generadaEl ?? DateTime.Now;
        foreach (var pagina in Paginas(documento))
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
    /// Recorre las páginas por índice.
    ///
    /// No con LINQ: PdfPages expone la colección por la interfaz **no
    /// genérica**, así que un `OfType&lt;PdfPage&gt;()` compila, no lanza nada y
    /// devuelve cero elementos. El pie de página estuvo ausente de todas las
    /// fichas por eso, sin un solo error que lo delatara.
    /// </summary>
    private static IEnumerable<PdfPage> Paginas(PdfDocument documento)
    {
        for (var i = 0; i < documento.PageCount; i++)
        {
            yield return documento.Pages[i];
        }
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
    /// La tabla de estancias, de la más reciente a la más antigua.
    ///
    /// Va al final a propósito: lo primero de una ficha de identificación es
    /// quién es y cómo se ve su identificación. Si alguien imprime solo la
    /// primera hoja, eso es lo que se lleva.
    /// </summary>
    private static void DibujarEstancias(
        Hoja hoja,
        IReadOnlyList<Estancia> estancias,
        XFont etiqueta,
        XFont valor,
        XFont pie)
    {
        hoja.Y += 6;

        // Se exige sitio para el título, el encabezado y unos cuantos renglones
        // antes de empezar. Si solo cabe uno, la tabla arranca al final de una
        // hoja y sigue en la otra: se lee peor que empezarla entera en la
        // siguiente.
        var minimo = 16 + 16 + AltoRenglonEstancia * Math.Min(estancias.Count, 3);
        hoja.AsegurarEspacio(minimo);

        hoja.Lienzo.DrawString(
            estancias.Count == 0 ? "Estancias" : $"Estancias ({estancias.Count})",
            etiqueta,
            XBrushes.Gray,
            new XPoint(Margen, hoja.Y));
        hoja.Y += 16;

        if (estancias.Count == 0)
        {
            hoja.Lienzo.DrawString("(ninguna registrada)", pie, XBrushes.Gray, new XPoint(Margen, hoja.Y));
            hoja.Y += 18;
            return;
        }

        DibujarEncabezadoEstancias(hoja, etiqueta);

        foreach (var estancia in estancias)
        {
            // Si el renglón cae en la página siguiente, el encabezado se repite:
            // una tabla que continúa sin sus títulos son cuatro columnas de
            // fechas sin saber cuál es la entrada y cuál la salida.
            if (hoja.AsegurarEspacio(AltoRenglonEstancia))
            {
                DibujarEncabezadoEstancias(hoja, etiqueta);
            }

            var columnas = new[]
            {
                FechaConHora(estancia.Ingreso),
                FechaConHora(estancia.Salida),
                estancia.Hospedado switch { 1 => "Sí", 0 => "No", _ => "—" },
                string.IsNullOrWhiteSpace(estancia.TipoAsignacion) ? "—" : estancia.TipoAsignacion!.Trim()
            };

            for (var i = 0; i < ColumnasEstancias.Length; i++)
            {
                hoja.Lienzo.DrawString(
                    columnas[i], valor, XBrushes.Black,
                    new XPoint(Margen + ColumnasEstancias[i].X, hoja.Y));
            }

            hoja.Y += AltoRenglonEstancia;
        }

        if (estancias.Count == TopeEstancias)
        {
            hoja.Y += 4;
            hoja.Lienzo.DrawString(
                $"Se muestran las últimas {TopeEstancias} estancias.",
                pie, XBrushes.Gray, new XPoint(Margen, hoja.Y));
            hoja.Y += 14;
        }
    }

    private static void DibujarEncabezadoEstancias(Hoja hoja, XFont etiqueta)
    {
        foreach (var (titulo, x) in ColumnasEstancias)
        {
            hoja.Lienzo.DrawString(titulo, etiqueta, XBrushes.Gray, new XPoint(Margen + x, hoja.Y));
        }

        hoja.Y += 4;
        hoja.Lienzo.DrawLine(XPens.LightGray, Margen, hoja.Y, hoja.Ancho - Margen, hoja.Y);
        hoja.Y += 12;
    }

    /// <summary>
    /// Con hora: en un hotel, dos estancias del mismo día se distinguen por la
    /// hora de entrada. (FIngreso y FSalida son numeric(18,8), así que la hora
    /// sí es fiable ahí — no como FechaAlta, que solo lleva un decimal.)
    /// </summary>
    private static string FechaConHora(DateTime? valor) =>
        valor?.ToString("yyyy-MM-dd HH:mm") ?? "—";

    private const double AltoRenglonEstancia = 16;

    /// <summary>Tope que aplica la API; solo se usa para avisar si se alcanzó.</summary>
    private const int TopeEstancias = 100;

    private static readonly (string Titulo, double X)[] ColumnasEstancias =
    {
        ("Ingreso", 0),
        ("Salida", 130),
        ("Hospedado", 260),
        ("Tipo", 340)
    };

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

        /// <returns>true si hubo que empezar una página nueva.</returns>
        public bool AsegurarEspacio(double alto)
        {
            if (Y + alto <= Fondo)
            {
                return false;
            }

            Lienzo.Dispose();
            Pagina = _documento.AddPage();
            Lienzo = XGraphics.FromPdfPage(Pagina);
            Y = Margen;
            return true;
        }

        /// <summary>
        /// El lienzo se tiene que soltar antes de guardar: PdfSharp escribe el
        /// contenido de la página al liberarlo.
        /// </summary>
        public void Cerrar() => Lienzo.Dispose();
    }
}
