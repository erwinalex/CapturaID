using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace SchId.Consulta.Nucleo;

/// <summary>Lo que sale mal al hablar con el servidor, en términos accionables.</summary>
public sealed class ErrorDeConsulta : Exception
{
    public ErrorDeConsulta(string mensaje, Exception? causa = null) : base(mensaje, causa) { }
}

/// <summary>
/// Habla con la API de SchId.
///
/// La consulta va por la API y no contra SQL Server directo por una razón
/// concreta: las imágenes nuevas viven como archivo en el disco **del
/// servidor**. Una aplicación en otra PC no puede leer esa carpeta sin un
/// recurso compartido de Windows, y compartir una carpeta llena de imágenes de
/// INE para que la lea cada terminal es peor que pedirlas por HTTP. De paso, la
/// cadena de conexión deja de andar suelta en cada terminal.
///
/// La validación del certificado se deja en manos de Windows: basta con
/// importar la CA en el almacén de raíces de cada terminal. Escribir una
/// validación propia es la forma más común de acabar aceptando cualquier
/// certificado sin enterarse, y aquí no hace falta.
/// </summary>
public sealed class ClienteSchId : IDisposable
{
    /// <summary>
    /// El tope de resultados que aplica el servidor. Se repite aquí solo para
    /// poder avisar "se muestran los primeros N"; si cambia en la API y no
    /// aquí, lo único que pasa es que el aviso deja de salir.
    /// </summary>
    public const int MaximoResultados = 200;

    /// <summary>El tope de estancias que aplica el servidor, para el mismo aviso.</summary>
    public const int MaximoEstancias = 100;

    private readonly HttpClient _http;
    private readonly DestinoServidor _destino;

    /// <summary>
    /// Para pruebas: recibe el HttpClient ya armado (el del servidor de pruebas
    /// en memoria) en lugar de abrir sockets. Así se verifica el cliente contra
    /// la API de verdad —rutas, cabeceras, códigos de estado— y no contra un
    /// doble que repetiría mis propias suposiciones.
    /// </summary>
    public ClienteSchId(DestinoServidor destino, HttpClient http)
    {
        _destino = destino;
        _http = http;
    }

    public ClienteSchId(DestinoServidor destino, string token, TimeSpan? tiempoLimite = null)
    {
        _destino = destino;

        var manejador = new SocketsHttpHandler
        {
            // Aquí ocurre el truco: la petición dice "schid-servidor" —que es lo
            // que se valida contra el certificado— y el socket se abre contra la
            // IP configurada. Es el equivalente del resolutor propio de la app
            // Android, y no toca la validación del certificado en absoluto.
            ConnectCallback = async (contexto, ct) =>
            {
                var host = contexto.DnsEndPoint.Host == DestinoServidor.NombreCertificado
                    ? _destino.Host
                    : contexto.DnsEndPoint.Host;

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(host, contexto.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

        _http = new HttpClient(manejador)
        {
            BaseAddress = new Uri(destino.UrlBase + "/"),
            Timeout = tiempoLimite ?? TimeSpan.FromSeconds(30)
        };

        _http.DefaultRequestHeaders.Add("X-Api-Key", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<ResultadoBusqueda>> BuscarAsync(
        string termino,
        CancellationToken ct = default)
    {
        var consulta = Uri.EscapeDataString(termino.Trim());
        if (consulta.Length == 0)
        {
            return Array.Empty<ResultadoBusqueda>();
        }

        using var respuesta = await EnviarAsync($"api/personas/buscar?q={consulta}", ct);
        Comprobar(respuesta);

        return await respuesta.Content.ReadFromJsonAsync<List<ResultadoBusqueda>>(cancellationToken: ct)
            ?? new List<ResultadoBusqueda>();
    }

    /// <summary>
    /// Los datos de una persona con sus dos imágenes, listos para la ficha.
    /// Devuelve null si esa persona ya no está.
    /// </summary>
    public async Task<FichaPersona?> ObtenerFichaAsync(long id, CancellationToken ct = default)
    {
        using var respuesta = await EnviarAsync($"api/personas/{id}", ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        Comprobar(respuesta);

        var datos = await respuesta.Content.ReadFromJsonAsync<FichaPersona>(cancellationToken: ct)
            ?? throw new ErrorDeConsulta("El servidor respondió sin contenido.");

        return datos with
        {
            Frente = await ImagenAsync(id, "frente", ct),
            Reverso = await ImagenAsync(id, "reverso", ct),
            Estancias = await EstanciasAsync(id, ct)
        };
    }

    /// <summary>
    /// Las estancias de una persona, de la más reciente a la más antigua.
    /// </summary>
    public async Task<IReadOnlyList<Estancia>> EstanciasAsync(long id, CancellationToken ct = default)
    {
        using var respuesta = await EnviarAsync($"api/personas/{id}/estancias", ct);
        Comprobar(respuesta);

        return await respuesta.Content.ReadFromJsonAsync<List<Estancia>>(cancellationToken: ct)
            ?? new List<Estancia>();
    }

    /// <summary>
    /// Una cara de la identificación. Que no esté es normal —hay personas sin
    /// imagen— así que un 404 devuelve null y no es un error.
    /// </summary>
    private async Task<ImagenIdentificacion?> ImagenAsync(long id, string lado, CancellationToken ct)
    {
        using var respuesta = await EnviarAsync($"api/personas/{id}/imagen/{lado}", ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        Comprobar(respuesta);

        var contenido = await respuesta.Content.ReadAsByteArrayAsync(ct);
        if (contenido.Length == 0)
        {
            return null;
        }

        var origen = respuesta.Headers.TryGetValues("X-Origen-Imagen", out var valores) &&
                     valores.FirstOrDefault() == nameof(OrigenImagen.ColumnaDeBaseDeDatos)
            ? OrigenImagen.ColumnaDeBaseDeDatos
            : OrigenImagen.Archivo;

        var descripcion = origen == OrigenImagen.Archivo
            ? "archivo en el servidor"
            : "columna de la base de datos (sistema anterior, sin migrar)";

        return new ImagenIdentificacion(contenido, origen, descripcion);
    }

    private async Task<HttpResponseMessage> EnviarAsync(string ruta, CancellationToken ct)
    {
        try
        {
            return await _http.GetAsync(ruta, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ErrorDeConsulta(Explicar(ex), ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ErrorDeConsulta(
                "El servidor tardó demasiado en contestar. Puede estar saturado o inalcanzable.", ex);
        }
    }

    /// <summary>
    /// Traduce el fallo a algo que se pueda actuar. La distinción importa porque
    /// manda a revisar lugares opuestos: el certificado se arregla en la
    /// terminal, y una conexión rechazada se arregla en el servidor.
    /// </summary>
    private string Explicar(HttpRequestException ex)
    {
        var texto = (ex.InnerException?.Message ?? ex.Message).ToLowerInvariant();

        if (texto.Contains("certificate") || texto.Contains("certificado") ||
            texto.Contains("trust") || texto.Contains("ssl"))
        {
            return "Esta PC no confía en el certificado del servidor.\n\n" +
                "Importa la CA en el almacén de raíces de confianza:\n" +
                "Import-Certificate -FilePath schid_ca.crt -CertStoreLocation Cert:\\LocalMachine\\Root";
        }

        return $"No se pudo conectar con el servidor ({_destino.Host}:{_destino.Puerto}).\n\n" +
            "Revisa la dirección, que el servicio esté corriendo y que el firewall permita el puerto.\n\n" +
            $"Detalle: {ex.Message}";
    }

    private static void Comprobar(HttpResponseMessage respuesta)
    {
        if (respuesta.IsSuccessStatusCode)
        {
            return;
        }

        throw new ErrorDeConsulta(respuesta.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "El servidor no reconoce el token. Revisa Servidor:Token en appsettings.json.",
            HttpStatusCode.Forbidden =>
                "El token no tiene el rol Consulta. Esta aplicación necesita un token de consulta, " +
                "no el del kiosko.",
            _ => $"El servidor respondió {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}."
        });
    }

    public void Dispose() => _http.Dispose();
}
