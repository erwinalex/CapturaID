using Microsoft.Data.SqlClient;
using SchId.Shared;

namespace SchId.Consulta.Nucleo;

/// <summary>
/// Consultas de solo lectura sobre dbo.Personas.
///
/// Esta aplicación **no escribe nada**. Es una consulta para el mostrador, y
/// que no tenga ni una sentencia de escritura es lo que garantiza que no puede
/// estropear datos de identificación por un clic mal dado.
/// </summary>
public sealed class RepositorioPersonas
{
    /// <summary>
    /// Tope de resultados. Con la tabla de varios años de un hotel, una
    /// búsqueda de "GARCIA" puede traer miles de renglones; nadie los lee, y
    /// traerlos hace lenta la consulta para todos. Quien no encuentre a su
    /// persona en 200 tiene que afinar la búsqueda, que además es más rápido.
    /// </summary>
    public const int MaximoResultados = 200;

    private readonly string _cadenaDeConexion;

    public RepositorioPersonas(string cadenaDeConexion) => _cadenaDeConexion = cadenaDeConexion;

    /// <summary>
    /// Busca por nombre o por CURP con un solo cuadro de texto: quien atiende no
    /// tiene por qué decidir de antemano qué está escribiendo.
    /// </summary>
    public async Task<IReadOnlyList<ResultadoBusqueda>> BuscarAsync(
        string texto,
        CancellationToken cancelacion = default)
    {
        var termino = texto.Trim();
        if (termino.Length == 0)
        {
            return Array.Empty<ResultadoBusqueda>();
        }

        await using var conexion = new SqlConnection(_cadenaDeConexion);
        await conexion.OpenAsync(cancelacion);

        await using var comando = new SqlCommand(
            $"""
             SELECT TOP ({MaximoResultados})
                    ID, Nombre, CURP, Nacionalidad, Edad, FechaAlta
             FROM   dbo.Personas
             WHERE  Nombre LIKE @patron OR CURP LIKE @patron
             ORDER  BY Nombre
             """, conexion);

        comando.Parameters.AddWithValue("@patron", $"%{EscaparLike(termino)}%");

        var resultados = new List<ResultadoBusqueda>();
        await using var lector = await comando.ExecuteReaderAsync(cancelacion);

        while (await lector.ReadAsync(cancelacion))
        {
            resultados.Add(new ResultadoBusqueda
            {
                Id = Convert.ToInt64(lector.GetValue(0)),
                Nombre = Texto(lector, 1),
                Curp = Texto(lector, 2),
                Nacionalidad = Texto(lector, 3),
                Edad = lector.IsDBNull(4) ? null : Convert.ToInt32(lector.GetValue(4)),
                FechaAlta = ConversionFechas.Delphi(Decimal(lector, 5))
            });
        }

        return resultados;
    }

    /// <summary>Los datos de una persona, sin imágenes (las trae [ResolutorImagenes]).</summary>
    public async Task<FichaPersona?> ObtenerAsync(long id, CancellationToken cancelacion = default)
    {
        await using var conexion = new SqlConnection(_cadenaDeConexion);
        await conexion.OpenAsync(cancelacion);

        await using var comando = new SqlCommand(
            """
            SELECT ID, Nombre, CURP, Direccion, Telefono, Nacionalidad, Edad,
                   Residencia, Origen, FechaAlta, UltimaModificacion
            FROM   dbo.Personas
            WHERE  ID = @id
            """, conexion);
        comando.Parameters.AddWithValue("@id", id);

        await using var lector = await comando.ExecuteReaderAsync(cancelacion);
        if (!await lector.ReadAsync(cancelacion))
        {
            return null;
        }

        return new FichaPersona
        {
            Id = Convert.ToInt64(lector.GetValue(0)),
            Nombre = Texto(lector, 1),
            Curp = Texto(lector, 2),
            Direccion = Texto(lector, 3),
            Telefono = Texto(lector, 4),
            Nacionalidad = Texto(lector, 5),
            Edad = lector.IsDBNull(6) ? null : Convert.ToInt32(lector.GetValue(6)),
            Residencia = Texto(lector, 7),
            Origen = Texto(lector, 8),
            FechaAlta = ConversionFechas.Delphi(Decimal(lector, 9)),
            UltimaModificacion = ConversionFechas.Delphi(Decimal(lector, 10))
        };
    }

    /// <summary>
    /// Las columnas de texto son nchar de largo fijo, así que vienen rellenas de
    /// espacios. Sin recortarlos, el nombre sale de la base con una cola de
    /// espacios que en el PDF descuadra todo.
    /// </summary>
    private static string? Texto(SqlDataReader lector, int columna) =>
        lector.IsDBNull(columna) ? null : lector.GetString(columna).Trim();

    private static decimal? Decimal(SqlDataReader lector, int columna) =>
        lector.IsDBNull(columna) ? null : Convert.ToDecimal(lector.GetValue(columna));

    /// <summary>
    /// En LIKE, %, _ y [ son comodines. Un apellido con guion bajo es raro, pero
    /// un usuario que escriba "%" esperando "todos" acabaría trayendo la tabla
    /// entera; escapándolos, el cuadro de búsqueda se comporta como texto y no
    /// como un lenguaje.
    /// </summary>
    internal static string EscaparLike(string texto) => texto
        .Replace("[", "[[]")
        .Replace("%", "[%]")
        .Replace("_", "[_]");
}
