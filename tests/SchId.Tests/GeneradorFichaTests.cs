using SchId.Consulta.Nucleo;

namespace SchId.Tests;

public class GeneradorFichaTests
{
    private static FichaPersona Ficha(
        ImagenIdentificacion? frente = null,
        ImagenIdentificacion? reverso = null) => new()
    {
        Id = 2777,
        Nombre = "JUAN PEREZ LOPEZ",
        Curp = "PELJ740115HDFRPN09",
        Direccion = "CALLE FALSA 123",
        Nacionalidad = "MEXICANA",
        Edad = 51,
        FechaAlta = new DateTime(2025, 3, 14),
        Frente = frente,
        Reverso = reverso
    };

    /// <summary>Un JPEG mínimo válido, para que PdfSharp tenga algo real que incrustar.</summary>
    private static byte[] JpegDePrueba()
    {
        // 1x1 píxel en JPEG, generado con: convert -size 1x1 xc:white jpg:-
        const string base64 =
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
            "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAHwAAAQUBAQEB" +
            "AQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1Fh" +
            "ByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZ" +
            "WmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXG" +
            "x8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/APn+iiiv/9k=";
        return Convert.FromBase64String(base64);
    }

    private static bool EsPdf(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';

    [Fact]
    public void Se_genera_un_PDF_de_verdad()
    {
        var pdf = new GeneradorFicha().Generar(Ficha());

        Assert.True(EsPdf(pdf), "los bytes no empiezan con la firma %PDF");
        Assert.True(pdf.Length > 500);
    }

    [Fact]
    public void La_ficha_sale_aunque_no_haya_ninguna_imagen()
    {
        var pdf = new GeneradorFicha().Generar(Ficha());

        Assert.True(EsPdf(pdf));
    }

    [Fact]
    public void Se_incrustan_las_dos_caras()
    {
        var jpeg = JpegDePrueba();

        var sinImagenes = new GeneradorFicha().Generar(Ficha());
        var conImagenes = new GeneradorFicha().Generar(Ficha(
            frente: new ImagenIdentificacion(jpeg, OrigenImagen.Archivo, "archivo"),
            reverso: new ImagenIdentificacion(jpeg, OrigenImagen.ColumnaDeBaseDeDatos, "columna")));

        Assert.True(EsPdf(conImagenes));
        Assert.True(
            conImagenes.Length > sinImagenes.Length,
            "el PDF con imágenes debería pesar más que el que no las lleva");
    }

    /// <summary>
    /// Una imagen corrupta en la base no debe impedir imprimir la ficha: es
    /// justo cuando más falta hace ver el resto de los datos.
    /// </summary>
    [Fact]
    public void Una_imagen_ilegible_no_tumba_la_ficha()
    {
        var basura = new byte[] { 0, 1, 2, 3, 4, 5 };

        var pdf = new GeneradorFicha().Generar(Ficha(
            frente: new ImagenIdentificacion(basura, OrigenImagen.ColumnaDeBaseDeDatos, "columna")));

        Assert.True(EsPdf(pdf));
    }

    [Fact]
    public void Los_campos_vacios_se_muestran_con_raya_y_no_se_omiten()
    {
        var campos = GeneradorFicha.Campos(new FichaPersona { Id = 1 }).ToList();

        Assert.Equal("1", campos.Single(c => c.Etiqueta == "ID").Valor);
        Assert.Equal("—", campos.Single(c => c.Etiqueta == "Nombre").Valor);
        Assert.Equal("—", campos.Single(c => c.Etiqueta == "Fecha de alta").Valor);
    }

    /// <summary>
    /// Las columnas son nchar de largo fijo: sin recortar, el nombre arrastra
    /// una cola de espacios que descuadra el PDF.
    /// </summary>
    [Fact]
    public void El_relleno_de_nchar_no_llega_al_PDF()
    {
        var campos = GeneradorFicha.Campos(new FichaPersona { Id = 1, Nombre = "JUAN PEREZ        " });

        Assert.Equal("JUAN PEREZ", campos.Single(c => c.Etiqueta == "Nombre").Valor);
    }

    /// <summary>
    /// Los domicilios de la INE traen calle, número, colonia, delegación y
    /// estado. Sin ajuste de línea se salen de la hoja y se pierde la mitad al
    /// imprimir — y eso solo aparece con datos reales.
    /// </summary>
    [Fact]
    public void Un_domicilio_largo_se_reparte_en_varios_renglones()
    {
        ResolutorFuentes.Instalar();
        using var documento = new PdfSharp.Pdf.PdfDocument();
        var lienzo = PdfSharp.Drawing.XGraphics.FromPdfPage(documento.AddPage());
        var fuente = new PdfSharp.Drawing.XFont(ResolutorFuentes.Familia, 11);

        var largo = "AVENIDA DE LOS INSURGENTES SUR NUMERO 1234 INTERIOR 5B, " +
                    "COLONIA DEL VALLE CENTRO, ALCALDIA BENITO JUAREZ, CIUDAD DE MEXICO, C.P. 03100";

        var renglones = GeneradorFicha.PartirEnRenglones(lienzo, largo, fuente, 350);

        Assert.True(renglones.Count > 1, "el domicilio largo debería partirse");
        Assert.All(renglones, r => Assert.True(lienzo.MeasureString(r, fuente).Width <= 350));
        Assert.Equal(largo.Split(' ').Length, renglones.Sum(r => r.Split(' ').Length));
    }

    /// <summary>
    /// Una palabra sola más ancha que la hoja no debe dejar el ciclo girando
    /// para siempre; se deja que se salga, que es mejor que colgarse.
    /// </summary>
    [Fact]
    public void Una_palabra_mas_ancha_que_la_hoja_no_cuelga_el_generador()
    {
        ResolutorFuentes.Instalar();
        using var documento = new PdfSharp.Pdf.PdfDocument();
        var lienzo = PdfSharp.Drawing.XGraphics.FromPdfPage(documento.AddPage());
        var fuente = new PdfSharp.Drawing.XFont(ResolutorFuentes.Familia, 11);

        var renglones = GeneradorFicha.PartirEnRenglones(lienzo, new string('X', 500), fuente, 100);

        Assert.Single(renglones);
    }

    /// <summary>
    /// Con domicilio largo y las dos imágenes, el contenido ya no cabe en una
    /// hoja. Si no hubiera salto de página, el reverso simplemente no saldría.
    /// </summary>
    [Fact]
    public void Cuando_no_cabe_todo_se_agrega_otra_pagina()
    {
        var jpeg = JpegDePrueba();
        var ficha = Ficha(
            frente: new ImagenIdentificacion(jpeg, OrigenImagen.Archivo, "archivo"),
            reverso: new ImagenIdentificacion(jpeg, OrigenImagen.Archivo, "archivo")) with
        {
            Direccion = string.Join(" ", Enumerable.Repeat("DOMICILIO MUY LARGO DE PRUEBA", 40))
        };

        var pdf = new GeneradorFicha().Generar(ficha);

        Assert.True(EsPdf(pdf));
        Assert.True(ContarPaginas(pdf) > 1, "debería haberse agregado una segunda página");
    }

    private static int ContarPaginas(byte[] pdf)
    {
        using var flujo = new MemoryStream(pdf);
        using var documento = PdfSharp.Pdf.IO.PdfReader.Open(flujo, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return documento.PageCount;
    }
}
