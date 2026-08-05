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
//   2) Corre primero con --dry-run para ver qué haría, sin tocar nada.
//   3) Después de correrlo (sin --dry-run), el espacio NO se libera solo:
//      hay que correr DBCC SHRINKFILE manualmente (instrucciones al final).
//
// Uso:
//   SchId.ImageMigration "<connectionString>" "<carpetaDestino>" [--dry-run]
//
// Ejemplo:
//   SchId.ImageMigration "Server=localhost\SQLEXPRESS;Database=SCHIDData;Trusted_Connection=True;TrustServerCertificate=True;" "C:\SchId\ImagenesINE" --dry-run

using System.Data;
using Microsoft.Data.SqlClient;
using SchId.Shared;

if (args.Length < 2)
{
    Console.WriteLine("Uso: SchId.ImageMigration \"<connectionString>\" \"<carpetaDestino>\" [--dry-run]");
    return 1;
}

var connectionString = args[0];
var basePath = args[1];
var dryRun = args.Contains("--dry-run");

Directory.CreateDirectory(basePath);

Console.WriteLine($"Carpeta destino: {basePath}");
Console.WriteLine(dryRun ? "Modo: DRY RUN (no se escribe nada, no se modifica la base)" : "Modo: EJECUCIÓN REAL");
Console.WriteLine();

using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

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
        var seGuardoFrente = await ExtraerImagenAsync(connection, id, "IDFoto1", ImageSide.Frente, basePath, dryRun);
        var seGuardoReverso = await ExtraerImagenAsync(connection, id, "IDFoto2", ImageSide.Reverso, basePath, dryRun);

        if (!dryRun && (seGuardoFrente || seGuardoReverso))
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

if (!dryRun && errores == 0 && migrados > 0)
{
    Console.WriteLine();
    Console.WriteLine("Para recuperar el espacio en disco, corre esto en SSMS (fuera de horario");
    Console.WriteLine("pico, y con un backup reciente ya hecho):");
    Console.WriteLine();
    Console.WriteLine("  DBCC SHRINKFILE (N'SCHIDData', 0);");
}

return errores == 0 ? 0 : 1;

static async Task<bool> ExtraerImagenAsync(
    SqlConnection connection, long id, string columna, ImageSide side, string basePath, bool dryRun)
{
    await using var cmd = new SqlCommand($"SELECT {columna} FROM dbo.Personas WHERE ID = @id", connection);
    cmd.Parameters.AddWithValue("@id", id);

    await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
    if (!await reader.ReadAsync() || await reader.IsDBNullAsync(0))
    {
        return false;
    }

    var path = ImagePathHelper.GetFullPath(basePath, id, side);

    if (dryRun)
    {
        Console.WriteLine($"  [dry-run] Se escribiría: {path}");
        return true;
    }

    await using var stream = reader.GetStream(0);
    await using var file = File.Create(path);
    await stream.CopyToAsync(file);
    return true;
}
