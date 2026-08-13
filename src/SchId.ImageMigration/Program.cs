// SchId.ImageMigration
//
// Herramienta de un solo uso: recorre dbo.Personas, extrae las imágenes que
// hoy están guardadas como binario en IDFoto1 (frente) e IDFoto2 (reverso),
// las escribe como archivo en disco (mismo esquema de nombres que usa la API,
// ver SchId.Shared.ImagePathHelper) y, si todo salió bien, deja esas dos
// columnas en NULL para poder recuperar el espacio en la base de datos.
//
// IMPORTANTE - léelo antes de correrlo en producción:
//   1) Haz un backup completo de la base de datos antes de correr esto.
//   2) Por omisión NO modifica nada: hay que pedir el borrado con --ejecutar.
//   3) Después de correrlo con --ejecutar, el espacio NO se libera solo:
//      hay que correr DBCC SHRINKFILE manualmente (instrucciones al final).
//
// Se puede volver a correr sin problema: las personas cuyas columnas ya
// quedaron en NULL no se vuelven a procesar.
//
// Uso:
//   SchId.ImageMigration "<connectionString>" "<carpetaDestino>" [--ejecutar]
//
// Ejemplo (simulación, que es lo que hace si no se pide otra cosa):
//   SchId.ImageMigration "Server=localhost\SQLEXPRESS;Database=SCHIDData;Trusted_Connection=True;TrustServerCertificate=True;" "C:\SchId\ImagenesINE"

using System.Data;
using Microsoft.Data.SqlClient;
using SchId.Shared;

if (args.Length < 2)
{
    Console.WriteLine("Uso: SchId.ImageMigration \"<connectionString>\" \"<carpetaDestino>\" [--ejecutar]");
    Console.WriteLine();
    Console.WriteLine("Sin --ejecutar solo simula: no escribe archivos ni modifica la base.");
    return 1;
}

var connectionString = args[0];
var basePath = args[1];

// El borrado se pide explícitamente. Antes bastaba con NO pasar --dry-run para
// que la herramienta borrara datos, o sea que el descuido salía caro; ahora el
// descuido no hace nada. Se sigue aceptando --dry-run para no romper lo que ya
// estaba documentado.
var ejecutar = args.Contains("--ejecutar") && !args.Contains("--dry-run");
var dryRun = !ejecutar;

Directory.CreateDirectory(basePath);

Console.WriteLine($"Carpeta destino: {basePath}");
Console.WriteLine(dryRun
    ? "Modo: SIMULACIÓN (no se escribe nada, no se modifica la base). Usa --ejecutar para hacerlo de verdad."
    : "Modo: EJECUCIÓN REAL - se vaciarán IDFoto1 e IDFoto2. ¿Tienes un backup reciente?");
Console.WriteLine();

using var connection = new SqlConnection(connectionString);

try
{
    await connection.OpenAsync();
}
catch (Exception ex)
{
    Console.WriteLine("No se pudo conectar a SQL Server.");
    Console.WriteLine($"  {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("Cosas que suelen ser:");
    Console.WriteLine("  - El nombre de la instancia. En la línea de comandos de Windows la barra");
    Console.WriteLine("    invertida NO se escapa, así que va sencilla: Server=localhost\\SQLEXPRESS");
    Console.WriteLine("    Si escribes dos, se mandan las dos y la instancia no se encuentra.");
    Console.WriteLine("  - El servicio SQL Server Browser detenido, o TCP/IP deshabilitado en");
    Console.WriteLine("    la configuración de SQL Server.");
    return 1;
}

var ids = new List<long>();
await using (var cmd = new SqlCommand(
    "SELECT ID FROM dbo.Personas WHERE IDFoto1 IS NOT NULL OR IDFoto2 IS NOT NULL", connection))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        ids.Add(reader.GetInt64(0));
    }
}

Console.WriteLine($"{ids.Count} persona(s) con imágenes por migrar.");
Console.WriteLine();

var migrados = 0;
var errores = 0;

foreach (var id in ids)
{
    try
    {
        var frente = await ExtraerImagenAsync(connection, id, "IDFoto1", ImageSide.Frente, basePath, dryRun);
        var reverso = await ExtraerImagenAsync(connection, id, "IDFoto2", ImageSide.Reverso, basePath, dryRun);

        // Solo se vacían las columnas cuando de verdad quedó algo en disco. Un
        // archivo de cero bytes con una columna que sí traía datos significa que
        // la escritura falló a medias, y ahí borrar el binario sería perder la
        // única copia.
        var sospechoso = (frente is 0) || (reverso is 0);
        if (sospechoso)
        {
            errores++;
            Console.WriteLine($"  PersonaId {id}: el archivo quedó vacío, no se toca la base. Revísalo a mano.");
            continue;
        }

        var hayAlgo = frente > 0 || reverso > 0;

        if (!dryRun && hayAlgo)
        {
            await using var updateCmd = new SqlCommand(
                "UPDATE dbo.Personas SET IDFoto1 = NULL, IDFoto2 = NULL WHERE ID = @id", connection);
            updateCmd.Parameters.AddWithValue("@id", id);
            await updateCmd.ExecuteNonQueryAsync();
        }

        migrados++;
        if (migrados % 100 == 0)
        {
            Console.WriteLine($"  ... {migrados}/{ids.Count}");
        }
    }
    catch (Exception ex)
    {
        errores++;
        Console.WriteLine($"  ERROR con PersonaId {id}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"Listo. Procesados: {migrados}, errores: {errores}.");

if (dryRun && migrados > 0)
{
    Console.WriteLine();
    Console.WriteLine("Esto fue una simulación. Cuando lo hayas revisado y tengas un backup");
    Console.WriteLine("reciente, vuelve a correrlo agregando --ejecutar al final.");
}

if (!dryRun && errores == 0 && migrados > 0)
{
    Console.WriteLine();
    Console.WriteLine("Para recuperar el espacio en disco, corre esto en SSMS (fuera de horario");
    Console.WriteLine("pico, y con un backup reciente ya hecho):");
    Console.WriteLine();
    Console.WriteLine("  DBCC SHRINKFILE (N'SCHIDData', 0);");
}

return errores == 0 ? 0 : 1;

/// <summary>
/// Extrae una columna de imagen a disco. Devuelve cuántos bytes se escribieron,
/// o null si la columna venía vacía. En simulación devuelve el tamaño que
/// tendría el archivo, sin escribir nada.
/// </summary>
static async Task<long?> ExtraerImagenAsync(
    SqlConnection connection, long id, string columna, ImageSide side, string basePath, bool dryRun)
{
    // El tamaño va PRIMERO en el SELECT por dos razones. Una, con
    // SequentialAccess las columnas se leen de izquierda a derecha, así que
    // ponerlo antes del binario deja que la simulación lo consulte sin traerse
    // megabytes por la red para tirarlos. Y dos, DATALENGTH devuelve int, no
    // bigint: sin el CAST, leerlo con GetInt64 revienta con
    // "Unable to cast object of type 'System.Int32' to type 'System.Int64'".
    await using var cmd = new SqlCommand(
        $"SELECT CAST(DATALENGTH({columna}) AS bigint), {columna} FROM dbo.Personas WHERE ID = @id",
        connection);
    cmd.Parameters.AddWithValue("@id", id);

    await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

    // DATALENGTH de una columna vacía es NULL, así que esto detecta de una vez
    // que no hay nada que extraer.
    if (!await reader.ReadAsync() || await reader.IsDBNullAsync(0))
    {
        return null;
    }

    var tamano = reader.GetInt64(0);
    var path = ImagePathHelper.GetFullPath(basePath, id, side);

    if (dryRun)
    {
        Console.WriteLine($"  [simulación] Se escribiría: {path} ({tamano} bytes)");
        return tamano;
    }

    await using (var stream = reader.GetStream(1))
    await using (var file = File.Create(path))
    {
        await stream.CopyToAsync(file);
    }

    return new FileInfo(path).Length;
}
