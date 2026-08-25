using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SchId.Api.Services;
using SchId.Shared;

namespace SchId.Tests;

/// <summary>
/// La lectura de las columnas [image] contra SQL Server de verdad.
///
/// No se puede cubrir con el proveedor en memoria: la entidad Persona no mapea
/// esas columnas a propósito, así que se leen con SQL crudo. Lo que se verifica
/// aquí —qué devuelve una columna [image] vacía frente a una con datos— es
/// comportamiento del motor.
/// </summary>
public class ImagenesHeredadasSqlTests : IAsyncLifetime
{
    private static readonly byte[] JpegFalso = { 0xFF, 0xD8, 0xFF, 0x01, 0x02 };

    private string? _cadena;

    /// <summary>
    /// Se comprueba que el servidor **responda**, no solo que la variable esté
    /// puesta. Con una variable apuntando a un servidor apagado, las pruebas
    /// fallaban en vez de saltarse, que es ruido sin información.
    /// </summary>
    public async Task InitializeAsync()
    {
        var cadena = Environment.GetEnvironmentVariable("SCHID_TEST_SQL");
        if (string.IsNullOrWhiteSpace(cadena))
        {
            return;
        }

        try
        {
            await using var conexion = new SqlConnection(cadena);
            await conexion.OpenAsync();
            _cadena = cadena;
        }
        catch (SqlException)
        {
            return;
        }

        await using var preparar = new SqlConnection(_cadena);
        await preparar.OpenAsync();

        await Ejecutar(preparar, "DELETE FROM dbo.Personas");

        await using var comando = new SqlCommand(
            """
            SET IDENTITY_INSERT dbo.Personas ON;
            INSERT INTO dbo.Personas (ID, Nombre, IDFoto1) VALUES (200, 'JUAN PEREZ', @foto);
            INSERT INTO dbo.Personas (ID, Nombre) VALUES (100, 'MARIA LOPEZ');
            SET IDENTITY_INSERT dbo.Personas OFF;
            """, preparar);
        comando.Parameters.AddWithValue("@foto", JpegFalso);
        await comando.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task Ejecutar(SqlConnection conexion, string sql)
    {
        await using var comando = new SqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    private ImagenesHeredadasSql Crear() => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SchIdDatabase"] = _cadena
            })
            .Build());

    [SkippableFact]
    public async Task La_imagen_del_sistema_anterior_se_lee_de_la_columna()
    {
        Skip.If(_cadena is null, "Sin SQL Server accesible en SCHID_TEST_SQL.");

        Assert.Equal(JpegFalso, await Crear().LeerAsync(200, ImageSide.Frente));
    }

    /// <summary>
    /// Una columna [image] en NULL tiene que verse como "no hay imagen", no como
    /// un arreglo vacío que luego se intente dibujar en el PDF.
    /// </summary>
    [SkippableFact]
    public async Task Una_columna_vacia_se_ve_como_ausencia_de_imagen()
    {
        Skip.If(_cadena is null, "Sin SQL Server accesible en SCHID_TEST_SQL.");

        Assert.Null(await Crear().LeerAsync(200, ImageSide.Reverso));
    }

    [SkippableFact]
    public async Task Una_persona_capturada_con_la_app_no_tiene_binario_en_la_columna()
    {
        Skip.If(_cadena is null, "Sin SQL Server accesible en SCHID_TEST_SQL.");

        Assert.Null(await Crear().LeerAsync(100, ImageSide.Frente));
    }

    [SkippableFact]
    public async Task Un_id_que_no_existe_no_revienta()
    {
        Skip.If(_cadena is null, "Sin SQL Server accesible en SCHID_TEST_SQL.");

        Assert.Null(await Crear().LeerAsync(999999, ImageSide.Frente));
    }
}
