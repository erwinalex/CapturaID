using System.Diagnostics;
using SchId.Consulta.Nucleo;
using SchId.Shared;

namespace SchId.Consulta;

/// <summary>
/// Buscar una persona y sacar su ficha.
///
/// La ventana no escribe nada en la base: es una consulta de mostrador, y que
/// no exista ni una sentencia de escritura es lo que garantiza que no puede
/// estropear datos de identificación por un clic mal dado.
/// </summary>
public sealed class VentanaPrincipal : Form
{
    private readonly RepositorioPersonas _repositorio;
    private readonly ResolutorImagenes _imagenes;
    private readonly GeneradorFicha _generador = new();
    private readonly string _carpetaImagenes;

    private readonly TextBox _busqueda = new();
    private readonly Button _botonBuscar = new();
    private readonly Button _botonFicha = new();
    private readonly DataGridView _lista = new();
    private readonly Label _estado = new();

    /// <summary>
    /// Las fichas se escriben aquí y la carpeta se borra al cerrar. Llevan datos
    /// personales: dejarlas tiradas en el escritorio o en Descargas es
    /// justamente lo que no se quiere.
    /// </summary>
    private readonly string _carpetaTemporal =
        Path.Combine(Path.GetTempPath(), "SchIdConsulta", Guid.NewGuid().ToString("N"));

    public VentanaPrincipal(string cadenaDeConexion, string carpetaImagenes)
    {
        _repositorio = new RepositorioPersonas(cadenaDeConexion);
        _carpetaImagenes = carpetaImagenes;
        _imagenes = new ResolutorImagenes(
            new ImagenesEnArchivo(carpetaImagenes),
            new ImagenesEnBaseDeDatos(cadenaDeConexion));

        Text = "SchId — Consulta de identificaciones";
        Width = 900;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 400);

        ArmarInterfaz();
    }

    private void ArmarInterfaz()
    {
        var superior = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            ColumnCount = 3,
            Padding = new Padding(10, 8, 10, 8)
        };
        superior.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        superior.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        superior.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _busqueda.Dock = DockStyle.Fill;
        _busqueda.PlaceholderText = "Nombre o CURP";
        // Enter busca: quien atiende teclea y busca sin soltar el teclado.
        _busqueda.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = BuscarAsync();
            }
        };

        _botonBuscar.Text = "Buscar";
        _botonBuscar.AutoSize = true;
        _botonBuscar.Click += (_, _) => _ = BuscarAsync();

        _botonFicha.Text = "Ver ficha (PDF)";
        _botonFicha.AutoSize = true;
        _botonFicha.Enabled = false;
        _botonFicha.Click += (_, _) => _ = GenerarFichaAsync();

        superior.Controls.Add(_busqueda, 0, 0);
        superior.Controls.Add(_botonBuscar, 1, 0);
        superior.Controls.Add(_botonFicha, 2, 0);

        _lista.Dock = DockStyle.Fill;
        _lista.ReadOnly = true;
        _lista.AllowUserToAddRows = false;
        _lista.AllowUserToDeleteRows = false;
        _lista.AllowUserToResizeRows = false;
        _lista.MultiSelect = false;
        _lista.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _lista.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _lista.RowHeadersVisible = false;
        _lista.SelectionChanged += (_, _) => _botonFicha.Enabled = SeleccionActual() is not null;
        // Doble clic saca la ficha: es lo que se espera de una lista.
        _lista.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                _ = GenerarFichaAsync();
            }
        };

        _estado.Dock = DockStyle.Bottom;
        _estado.Height = 24;
        _estado.Padding = new Padding(10, 4, 10, 4);
        _estado.Text = AvisoDeCarpeta() ?? "Escribe un nombre o un CURP y pulsa Buscar.";

        Controls.Add(_lista);
        Controls.Add(_estado);
        Controls.Add(superior);

        AcceptButton = _botonBuscar;
        FormClosed += (_, _) => LimpiarTemporales();
    }

    /// <summary>
    /// Avisa si la carpeta de imágenes no está donde dice la configuración.
    ///
    /// Sin este aviso, el síntoma sería que las fichas de las personas
    /// capturadas con la app móvil salen sin imagen mientras las del sistema
    /// anterior salen bien — y eso apunta a la app móvil, que no tiene nada que
    /// ver.
    /// </summary>
    private string? AvisoDeCarpeta()
    {
        if (string.IsNullOrWhiteSpace(_carpetaImagenes))
        {
            return "Aviso: no hay ImageStorage:BasePath configurado. Solo se verán las imágenes " +
                "que sigan en la base de datos.";
        }

        if (!Directory.Exists(_carpetaImagenes))
        {
            return $"Aviso: no se encuentra la carpeta de imágenes ({_carpetaImagenes}). " +
                "Solo se verán las imágenes que sigan en la base de datos.";
        }

        return null;
    }

    private async Task BuscarAsync()
    {
        var termino = _busqueda.Text.Trim();
        if (termino.Length == 0)
        {
            _estado.Text = "Escribe un nombre o un CURP.";
            return;
        }

        using var espera = new EsperaVisible(this);
        _botonBuscar.Enabled = false;
        _estado.Text = "Buscando...";

        try
        {
            var resultados = await _repositorio.BuscarAsync(termino);
            MostrarResultados(resultados);
        }
        catch (Exception ex)
        {
            _estado.Text = "No se pudo consultar la base de datos.";
            MessageBox.Show(
                $"No se pudo consultar la base de datos.\n\n{ex.Message}\n\n" +
                "Revisa la cadena de conexión en appsettings.json y que el servidor esté disponible.",
                "SchId Consulta",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _botonBuscar.Enabled = true;
        }
    }

    private void MostrarResultados(IReadOnlyList<ResultadoBusqueda> resultados)
    {
        _lista.DataSource = resultados
            .Select(r => new
            {
                r.Id,
                Nombre = r.Nombre ?? "",
                CURP = r.Curp ?? "",
                Nacionalidad = r.Nacionalidad ?? "",
                Edad = r.Edad?.ToString() ?? "",
                Alta = r.FechaAlta?.ToString("yyyy-MM-dd") ?? ""
            })
            .ToList();

        // El ID es corto y no debe llevarse una quinta parte del ancho; el
        // nombre es lo que se lee.
        if (_lista.Columns["Id"] is { } columnaId)
        {
            columnaId.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            columnaId.HeaderText = "ID";
        }

        _botonFicha.Enabled = SeleccionActual() is not null;

        _estado.Text = resultados.Count switch
        {
            0 => "Sin resultados.",
            RepositorioPersonas.MaximoResultados =>
                $"Se muestran los primeros {RepositorioPersonas.MaximoResultados}. Afina la búsqueda si no está quien buscas.",
            1 => "1 resultado.",
            var n => $"{n} resultados."
        };
    }

    private long? SeleccionActual()
    {
        if (_lista.CurrentRow?.Cells["Id"]?.Value is { } valor)
        {
            return Convert.ToInt64(valor);
        }

        return null;
    }

    private async Task GenerarFichaAsync()
    {
        if (SeleccionActual() is not { } id)
        {
            return;
        }

        using var espera = new EsperaVisible(this);
        _botonFicha.Enabled = false;
        _estado.Text = "Generando la ficha...";

        try
        {
            var ficha = await _repositorio.ObtenerAsync(id);
            if (ficha is null)
            {
                _estado.Text = "Esa persona ya no está en la base de datos.";
                return;
            }

            ficha = ficha with
            {
                Frente = await _imagenes.ObtenerAsync(id, ImageSide.Frente),
                Reverso = await _imagenes.ObtenerAsync(id, ImageSide.Reverso)
            };

            var pdf = _generador.Generar(ficha);

            Directory.CreateDirectory(_carpetaTemporal);
            var ruta = Path.Combine(_carpetaTemporal, $"ficha_{id}.pdf");
            await File.WriteAllBytesAsync(ruta, pdf);

            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });

            _estado.Text = ficha.Frente is null && ficha.Reverso is null
                ? "Ficha generada. Esta persona no tiene imágenes ni en archivo ni en la base."
                : $"Ficha generada: {Describir(ficha)}";
        }
        catch (Exception ex)
        {
            _estado.Text = "No se pudo generar la ficha.";
            MessageBox.Show(
                $"No se pudo generar la ficha.\n\n{ex.Message}",
                "SchId Consulta",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _botonFicha.Enabled = SeleccionActual() is not null;
        }
    }

    /// <summary>
    /// Decir de dónde salió cada imagen sirve durante la transición: es la forma
    /// de ver qué personas ya están migradas sin entrar a la base a mano.
    /// </summary>
    private static string Describir(FichaPersona ficha)
    {
        var partes = new List<string>();

        if (ficha.Frente is { } frente)
        {
            partes.Add($"frente desde {Nombrar(frente.Origen)}");
        }

        if (ficha.Reverso is { } reverso)
        {
            partes.Add($"reverso desde {Nombrar(reverso.Origen)}");
        }

        return string.Join(", ", partes);
    }

    private static string Nombrar(OrigenImagen origen) =>
        origen == OrigenImagen.Archivo ? "archivo" : "la base de datos";

    private void LimpiarTemporales()
    {
        try
        {
            if (Directory.Exists(_carpetaTemporal))
            {
                Directory.Delete(_carpetaTemporal, recursive: true);
            }
        }
        catch (IOException)
        {
            // Si el visor todavía tiene el PDF abierto, Windows no deja
            // borrarlo. No vale la pena molestar al usuario al cerrar; queda en
            // la carpeta temporal, que es donde Windows limpia solo.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Cursor de espera mientras dura una consulta.</summary>
    private sealed class EsperaVisible : IDisposable
    {
        private readonly Form _ventana;

        public EsperaVisible(Form ventana)
        {
            _ventana = ventana;
            _ventana.Cursor = Cursors.WaitCursor;
        }

        public void Dispose() => _ventana.Cursor = Cursors.Default;
    }
}
