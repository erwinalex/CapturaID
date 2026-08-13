using System.Collections.Concurrent;
using Microsoft.AspNetCore.Connections;

namespace SchId.Api.Diagnostico;

/// <summary>
/// Registra en el log cada conexión TCP y cada petición HTTP.
///
/// Existe para un caso que de otro modo es invisible: cuando el saludo TLS
/// falla, la conexión se abre y se cierra **sin llegar a producir ninguna
/// petición HTTP**. El log de peticiones —que es lo único que se ve por
/// omisión— no tiene nada que mostrar, así que desde el servidor parece que el
/// cliente nunca llamó. Contar las peticiones por conexión hace que ese caso se
/// distinga solo:
///
///     [conexion] abierta desde 192.168.1.55:41234
///     [conexion] cerrada 192.168.1.55:41234 tras 28 ms, SIN peticiones HTTP
///
/// Una conexión sin peticiones es un saludo que no cuajó. Para ver el motivo
/// exacto hay que subir a Debug el nivel de Microsoft.AspNetCore.Server.Kestrel
/// —ahí es donde Kestrel escribe el error del handshake— pero para saber que el
/// problema está ahí basta con esto.
/// </summary>
public sealed class RegistroDeConexiones
{
    private readonly ConcurrentDictionary<string, int> _peticiones = new();

    /// <summary>
    /// Se asigna después de construir la aplicación. El middleware de conexión
    /// se registra antes de que exista el contenedor de servicios, así que no
    /// hay de dónde resolver un logger en ese momento.
    /// </summary>
    public ILogger? Registro { get; set; }

    public void ContarPeticion(string idConexion) =>
        _peticiones.AddOrUpdate(idConexion, 1, (_, n) => n + 1);

    public async Task AtenderConexionAsync(ConnectionContext contexto, Func<Task> siguiente)
    {
        var remoto = contexto.RemoteEndPoint?.ToString() ?? "(desconocido)";
        var inicio = DateTimeOffset.UtcNow;

        _peticiones[contexto.ConnectionId] = 0;
        Registro?.LogInformation("[conexion] abierta desde {Remoto}", remoto);

        try
        {
            await siguiente();
        }
        catch (Exception ex)
        {
            Registro?.LogWarning("[conexion] error con {Remoto}: {Mensaje}", remoto, ex.Message);
            throw;
        }
        finally
        {
            _peticiones.TryRemove(contexto.ConnectionId, out var cuantas);
            var duracion = (DateTimeOffset.UtcNow - inicio).TotalMilliseconds;

            if (cuantas == 0)
            {
                Registro?.LogWarning(
                    "[conexion] cerrada {Remoto} tras {Duracion:F0} ms, SIN peticiones HTTP. " +
                    "Suele ser un saludo TLS que no se completó: el cliente no confía en el " +
                    "certificado, o pide una versión de TLS que este servidor no ofrece. " +
                    "Sube Microsoft.AspNetCore.Server.Kestrel a Debug para ver el motivo exacto.",
                    remoto, duracion);
            }
            else
            {
                Registro?.LogInformation(
                    "[conexion] cerrada {Remoto} tras {Duracion:F0} ms, {Cuantas} petición(es).",
                    remoto, duracion, cuantas);
            }
        }
    }
}

/// <summary>
/// Registra cada petición HTTP con su desenlace, y avisa al
/// <see cref="RegistroDeConexiones"/> de que la conexión sí llegó a usarse.
/// </summary>
public class RegistroDePeticiones
{
    private readonly RequestDelegate _siguiente;
    private readonly RegistroDeConexiones _conexiones;
    private readonly ILogger<RegistroDePeticiones> _registro;

    public RegistroDePeticiones(
        RequestDelegate siguiente,
        RegistroDeConexiones conexiones,
        ILogger<RegistroDePeticiones> registro)
    {
        _siguiente = siguiente;
        _conexiones = conexiones;
        _registro = registro;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        _conexiones.ContarPeticion(contexto.Connection.Id);

        var inicio = DateTimeOffset.UtcNow;
        var remoto = contexto.Connection.RemoteIpAddress?.ToString() ?? "(desconocido)";
        var ruta = Ocultar(contexto.Request.Path);

        try
        {
            await _siguiente(contexto);
        }
        finally
        {
            var duracion = (DateTimeOffset.UtcNow - inicio).TotalMilliseconds;
            var quien = contexto.User.Identity?.Name;
            var codigo = contexto.Response.StatusCode;

            var explicacion = codigo switch
            {
                401 => " (sin token o token no reconocido)",
                403 => " (el token no tiene el rol necesario)",
                _ => ""
            };

            _registro.LogInformation(
                "[peticion] {Metodo} {Ruta} desde {Remoto}{Token} -> {Codigo}{Explicacion} en {Duracion:F0} ms",
                contexto.Request.Method,
                ruta,
                remoto,
                quien is null ? "" : $" [{quien}]",
                codigo,
                explicacion,
                duracion);
        }
    }

    /// <summary>
    /// El CURP va en la ruta de la consulta, y el log no es lugar para datos
    /// personales. Se registra qué endpoint se llamó, no a quién se buscó.
    /// </summary>
    private static string Ocultar(PathString ruta)
    {
        var texto = ruta.Value ?? "";
        const string prefijo = "/api/personas/curp/";

        return texto.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
            ? prefijo + "***"
            : texto;
    }
}
