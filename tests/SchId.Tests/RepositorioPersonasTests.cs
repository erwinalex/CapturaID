using Microsoft.Data.SqlClient;
using SchId.Consulta.Nucleo;
using SchId.Shared;

namespace SchId.Tests;

/// <summary>
/// Pruebas contra SQL Server de verdad, no contra un doble.
///
/// Lo que se está verificando aquí es precisamente lo que un doble no puede
/// contestar: cómo se comporta LIKE con el relleno de nchar, qué devuelve una
/// columna [image] vacía, y si la conversión del TDateTime de Delphi coincide
/// con la fecha real. Todos esos son detalles del motor.
///
/// Se saltan solas si no hay servidor: la cadena se toma de SCHID_TEST_SQL.
/// </summary>
public class RepositorioPersonasTests : IAsyncLifetime
{
    private readonly string? _cadena = Environment.GetEnvironmentVariable("SCHID_TEST_SQL");

    private bool HayServidor => !string.IsNullOrWhiteSpace(_cadena);

    private static readonly byte[] JpegFalso = { 0xFF, 0xD8, 0xFF, 0x01, 0x02 };

    public async Task InitializeAsync()
    {
        if (!HayServidor)
        {
            return;
        }

        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();

        await Ejecutar(conexion, "DELETE FROM dbo.Personas");

        // Persona capturada con la app móvil: sin binario en las columnas.
        await Ejecutar(conexion,
            """
            SET IDENTITY_INSERT dbo.Personas ON;
            INSERT INTO dbo.Personas (ID, Nombre, CURP, Nacionalidad, Edad, Direccion, FechaAlta)
            VALUES (100, 'MARIA LOPEZ HERNANDEZ', 'LOHM850315MDFPRR03', 'MEXICANA', 40,
                    'AV SIEMPRE VIVA 742', 45730.0);
            SET IDENTITY_INSERT dbo.Personas OFF;
            """);

        // Persona del sistema anterior: la imagen vive en la columna.
        await using (var comando = new SqlCommand(
            """
            SET IDENTITY_INSERT dbo.Personas ON;
            INSERT INTO dbo.Personas (ID, Nombre, CURP, Nacionalidad, IDFoto1)
            VALUES (200, 'JUAN PEREZ GARCIA', 'PEGJ740115HDFRRN08', 'MEXICANA', @foto);
            SET IDENTITY_INSERT dbo.Personas OFF;
            """, conexion))
        {
            comando.Parameters.AddWithValue("@foto", JpegFalso);
            await comando.ExecuteNonQueryAsync();
        }

        // Alguien con un carácter que LIKE trata como comodín.
        await Ejecutar(conexion,
            """
            SET IDENTITY_INSERT dbo.Personas ON;
            INSERT INTO dbo.Personas (ID, Nombre, CURP) VALUES (300, 'ANA 100% SEGURA', 'XXXX000000XXXXXX00');
            SET IDENTITY_INSERT dbo.Personas OFF;
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task Ejecutar(SqlConnection conexion, string sql)
    {
        await using var comando = new SqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task Se_busca_por_nombre()
    {
        Skip.IfNot(HayServidor, "Sin SCHID_TEST_SQL configurado.");

        var resultados = await new RepositorioPersonas(_cadena!).BuscarAsync("lopez");

        Assert.Single(resultados);
        Assert.Equal("MARIA LOPEZ HERNANDEZ", resultados[0].Nombre);
    }

    [SkippableFact]
    public async Task Se_busca_por_CURP_con_el_mismo_cuadro()
    {
        Skip.IfNot(HayServidor, "Sin SCHID_TEST_SQL configurado.");

        var resultados = await new RepositorioPersonas(_cadena!).BuscarAsync("PEGJ740115");

        Assert.Single(resultados);
        Assert.Equal(200, resultados[0].Id);
    }

    /// <summary>
    /// Las columnas son nchar: el nombre viene con una cola de espacios que, sin
    /// recortar, descuadra la lista y el PDF.
    /// </summary>
    [SkippableFact]
    public async Task El_relleno_de_nchar_no_sale_en_los_resultados()
    {
        Skip.IfNot(HayServidor, "Sin SCHID_TEST_SQL configurado.");

        var resultados = await new RepositorioPersonas(_cadena!).BuscarAsync("lopez");

        Assert.DoesNotContain("  ", resultados[0].Nombre);
        Assert.Equal(resultados[0].Nombre, resultados[0].Nombre!.Trim());
    }

    /// <summary>
    /// Quien escriba "%" en el cuadro espera buscar ese carácter, no traerse la
    /// tabla entera.
    /// </summary>
    [SkippableFact]
    public async Task Un_comodin_escrito_por_el_usuario_se_trata_como_texto()
    {
        Skip.IfNot(HayServidor, "Sin SCHID_TEST_SQL configurado.");

        var repositorio = new RepositorioPersonas(_cadena!);

        var conComodin = await repositorio.BuscarAsync("%");
        var literal = await repositorio.BuscarAsync("100%");

        Assert.Single(conComodin);
        Assert.Equal(300, conComodin[0].Id);
        Assert.Single(literal);
    }

    [SkippableFact]
    public async Task Una_busqueda_vacia_no_trae_nada()
    {
        Skip.IfNot(HayServidor, "Sin SCHID_TEST_SQL configurado.");

        Assert.Empty(await new RepositorioPersonas(_cadena!).BuscarAsync("   "));
    }

    /// <summary>
    /// 45730.0 en TDateTime es el 2025-03-14. Con el CAST de SQL Server saldría
    /// dos días corrido, que es la trampa que documenta DelphiDateTime.
    /// </summary>
    [SkippableFact]
    public async Task La_fecha_de_Delphi_se_convierte_bien_leyendo_de_la_base()
    {
        Skip.IfNot(HayServidor, "Sin SCHID_TEST_SQL configurado.");

        var ficha = await new RepositorioPersonas(_cadena!).ObtenerAsync(100);

        Assert.Equal(new DateTime(2025, 3, 14), ficha!.FechaAlta);
    }

    [SkippableFact]
    public async Task La_imagen_del_sistema_anterior_se_lee_de_la_columna()
    {
        Skip.IfNot(HayServidor, "Sin SCHID_TEST_SQL configurado.");

        var fuente = new ImagenesEnBaseDeDatos(_cadena!);

        Assert.Equal(JpegFalso, await fuente.LeerAsync(200, ImageSide.Frente));
        Assert.Null(await fuente.LeerAsync(200, ImageSide.Reverso));
    }

    [SkippableFact]
    public async Task Una_persona_capturada_con_la_app_no_tiene_binario_en_la_columna()
    {
        Skip.IfNot(HayServidor, "Sin SCHID_TEST_SQL configurado.");

        var fuente = new ImagenesEnBaseDeDatos(_cadena!);

        Assert.Null(await fuente.LeerAsync(100, ImageSide.Frente));
    }

    [SkippableFact]
    public async Task Un_id_que_no_existe_devuelve_nulo()
    {
        Skip.IfNot(HayServidor, "Sin SCHID_TEST_SQL configurado.");

        Assert.Null(await new RepositorioPersonas(_cadena!).ObtenerAsync(999999));
    }
}
