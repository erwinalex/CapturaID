# SchId - API de registro de INE

Reemplazo del servicio REST (antes en Delphi + mORMot) para capturar los datos
de la INE de los huéspedes desde la app Android y registrarlos en la base
`SCHIDData` (SQL Server Express) ya existente, sin acumular las imágenes como
binario en la base de datos.

**No pude compilar este proyecto en el entorno donde lo generé** (no había
SDK de .NET disponible ni acceso a NuGet). Antes de usarlo, en tu máquina con
Visual Studio / `dotnet` instalado, corre `dotnet build` desde la raíz y
revisa que no haya errores. Si `dotnet restore` se queja de que no encuentra
alguna versión exacta de un paquete (`Microsoft.EntityFrameworkCore.SqlServer`,
`Microsoft.Data.SqlClient`, `Swashbuckle.AspNetCore`, etc.), es solo cuestión
de versión: corre `dotnet add package <nombre>` sin especificar versión para
que tome la última estable, o ajusta el número en el `.csproj`.

## Estructura

- `src/SchId.Shared` — código compartido entre la API y la herramienta de
  migración: conversión de fechas Delphi (`DelphiDateTime`) y el cálculo de
  nombres de archivo de imagen (`ImagePathHelper`).
- `src/SchId.Api` — el servicio REST (ASP.NET Core). Se instala como Windows
  Service en la misma PC donde está SQL Server Express.
- `src/SchId.ImageMigration` — herramienta de línea de comandos, de un solo
  uso, para sacar las imágenes que hoy están guardadas como binario en
  `Personas.IDFoto1` / `IDFoto2` y dejarlas como archivos en disco.
- `sql/verificar_migracion.sql` — consultas para revisar el espacio ocupado
  antes/después de migrar, y para ver quién ya pasó el periodo de retención.

## Decisiones de diseño (resumen de lo que platicamos)

- Las imágenes NUNCA se guardan en la base de datos. Se guardan en disco, con
  nombre calculado a partir del `ID` de la persona (`{ID}_frente.jpg`,
  `{ID}_reverso.jpg`). No se guarda ninguna ruta en la tabla `Personas` — ni
  siquiera se usa el campo `Ocupacion`.
- Las fechas de `Estancias` (`FIngreso`, `FSalida`) y `Personas.FechaAlta`
  están en formato `TDateTime` de Delphi (numeric), no en `DATETIME` nativo de
  SQL Server. La conversión correcta vive en `DelphiDateTime` — **no uses
  `CAST(columna AS DATETIME)` directo en T-SQL**, da un resultado desfasado
  2 días.
- La retención (`RetentionCleanupService`) se calcula por la ÚLTIMA salida de
  cada persona, no por la fecha de captura de la imagen, porque así lo exige
  la regla de negocio (huéspedes recurrentes reinician el periodo). Empieza
  con el borrado automático DESACTIVADO (`HabilitarBorradoAutomatico: false`
  en `appsettings.json`) — el job solo registra en el log quiénes serían
  candidatos, para que puedas revisarlo antes de activar el borrado real.
- La cadena de conexión usa autenticación integrada de Windows
  (`Trusted_Connection=True`), ya que la API y SQL Server Express corren en la
  misma PC — así no hay contraseña de base de datos que proteger.

## Cómo correrlo la primera vez

1. Abre `SchId.sln` en Visual Studio (o `code .` si prefieres VS Code con la
   extensión de C#).
2. Edita `src/SchId.Api/appsettings.json`:
   - `ConnectionStrings:SchIdDatabase` con el nombre real de tu instancia de
     SQL Server Express si no es `localhost\SQLEXPRESS`.
   - `ImageStorage:BasePath` con la carpeta donde quieres guardar las
     imágenes (se crea sola si no existe).
3. `dotnet build` desde la raíz del repo para confirmar que compila.
4. Antes de tocar datos reales, corre la migración en modo simulación:

   ```
   cd src/SchId.ImageMigration
   dotnet run -- "Server=localhost\SQLEXPRESS;Database=SCHIDData;Trusted_Connection=True;TrustServerCertificate=True;" "C:\SchId\ImagenesINE" --dry-run
   ```

   Revisa la salida: te dice cuántas personas tienen imágenes y qué archivos
   escribiría, sin tocar nada todavía.

5. **Haz un backup completo de `SCHIDData` antes del siguiente paso.**
6. Corre la migración real (quitando `--dry-run`). Al terminar, sigue la
   instrucción que imprime en pantalla para correr `DBCC SHRINKFILE` en SSMS
   y recuperar el espacio en disco.
7. Corre `sql/verificar_migracion.sql` antes y después para confirmar el
   espacio liberado y que ya no queden imágenes en la tabla.

## Cómo instalarlo como servicio de Windows

```
dotnet publish src/SchId.Api -c Release -o C:\SchId\Api --self-contained false
sc create SchIdApi binPath= "C:\SchId\Api\SchId.Api.exe"
sc start SchIdApi
```

## Pendientes / siguiente paso

- Definir y construir los endpoints que consumirá la app Android más allá del
  registro básico (`POST /api/personas/registro`, `GET /api/personas/curp/{curp}`,
  `POST /api/personas/{id}/checkout`) — por ejemplo, listar estancias activas.
- HTTPS con certificado real o autofirmado para la red local (por ahora usa
  el certificado de desarrollo de .NET).
- Revisar con calma el criterio de `RetentionCleanupService` (y validarlo con
  quien lleve el tema legal) antes de activar `HabilitarBorradoAutomatico`.
- Empezar el proyecto Android (Kotlin + CameraX + ML Kit) que consume esta
  API.
