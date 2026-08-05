# SchId - API de registro de INE

Reemplazo del servicio REST (antes en Delphi + mORMot) para capturar los datos
y las imágenes de la INE de los huéspedes desde un kiosko Android y guardarlos
en la base `SCHIDData` (SQL Server Express) ya existente, sin acumular las
imágenes como binario en la base de datos.

## Alcance: qué hace esta API y qué no

Esta API **solo captura**. Registra a la persona en `dbo.Personas` y guarda sus
dos fotos de INE en disco. Nada más.

El **PMS es el dueño de las estancias**: él consulta la base de datos para
recuperar los nombres, asigna la habitación y edita la estancia para notificar
la salida. La API no escribe una sola fila en `dbo.Estancias` — la única parte
que la toca es el job de retención, y solo para leerla. El puente entre ambos
sistemas es el `Id` que devuelve `POST /api/personas/registro`: con ese Id el
PMS amarra a la persona con la estancia que él crea.

El cliente es un **kiosko desatendido**: no hay nadie que capture usuario y
contraseña al arrancar, y la pantalla no muestra datos de huéspedes, solo lo que
se está capturando en ese momento. Ese par de restricciones explica dos
decisiones que se ven raras fuera de contexto: la autenticación es por token
fijo, y la respuesta al kiosko no incluye ningún dato personal.

## Estructura

- `src/SchId.Shared` — código compartido entre la API y la herramienta de
  migración: conversión de fechas Delphi (`DelphiDateTime`) y el cálculo de
  nombres de archivo de imagen (`ImagePathHelper`).
- `src/SchId.Api` — el servicio REST (ASP.NET Core). Se instala como Windows
  Service en la misma PC donde está SQL Server Express.
- `src/SchId.ImageMigration` — herramienta de línea de comandos, de un solo
  uso, para sacar las imágenes que hoy están guardadas como binario en
  `Personas.IDFoto1` / `IDFoto2` y dejarlas como archivos en disco.
- `tests/SchId.Tests` — pruebas unitarias y de integración (`dotnet test`).
- `sql/verificar_migracion.sql` — consultas para revisar el espacio ocupado
  antes/después de migrar, y para ver quién ya pasó el periodo de retención.

## Endpoints

### `POST /api/personas/registro` — rol `Captura`

`multipart/form-data` con los datos leídos del PDF417 del reverso de la INE, más
las imágenes `imagenFrente` e `imagenReverso` (JPEG).

El servidor busca el CURP y decide qué hacer, **la app no tiene que preguntar
primero si la persona existe**:

| Situación | Qué pasa | `resultado` |
|---|---|---|
| El CURP no existe | Se da de alta | `Creado` |
| Existe y algún dato cambió | Se actualiza el registro previo | `Actualizado` |
| Existe y todo coincide | No se toca la fila | `SinCambios` |

La comparación se hace en el servidor a propósito. Hacerla en la app obligaría a
mandarle los datos previos del huésped a una tableta que está en un mostrador,
y ahí no pintan nada. La respuesta solo lleva el `id`, el `resultado` y los
**nombres** de los campos que cambiaron — nunca sus valores:

```json
{
  "id": 1523,
  "resultado": "Actualizado",
  "camposActualizados": ["Direccion", "Edad"],
  "imagenFrenteGuardada": true,
  "imagenReversoGuardada": true
}
```

Dos reglas de la comparación que conviene tener presentes:

- Los campos de la tabla son `nchar`, así que SQL Server los devuelve rellenados
  con espacios. La comparación recorta ese relleno; si no, cada captura
  reportaría todos los campos como cambiados.
- **Un campo que llegue vacío se interpreta como "no se pudo leer", no como
  "bórralo"**, y se conserva el valor anterior. Así un PDF417 rayado o un
  escaneo malo no borra datos buenos que ya estaban en la base.

Las imágenes sí se reemplazan siempre que vengan, porque son las vigentes. Se
rechaza (400) cualquier archivo que no empiece con la firma de un JPEG, y se
valida antes de tocar la base para no dejar personas dadas de alta sin fotos.

### `GET /api/personas/curp/{curp}` — rol `Consulta`

Devuelve los datos completos de la persona. **El kiosko no puede llamarlo**: su
token no tiene este rol. Está para soporte y herramientas administrativas; el
PMS lee la base directamente.

## Autenticación

Token fijo en el encabezado `X-Api-Key`, configurado en la sección
`Autenticacion` de `appsettings.json`. Se eligió esto en vez de usuario y
contraseña porque el kiosko es desatendido: el token se provisiona una vez en el
dispositivo.

Cada token lleva sus roles, y hay dos:

- **`Captura`** — solo puede registrar capturas. Es el del kiosko.
- **`Consulta`** — puede leer datos de personas.

Están separados por una razón concreta: si alguien se lleva la tableta y le saca
el token, con `Captura` puede dar de alta registros, pero **no descargarse el
padrón de huéspedes**.

Es una lista y no un solo token para poder darle uno distinto a cada kiosko y
revocar el de un equipo extraviado sin dejar fuera a los demás.

```json
"Autenticacion": {
  "Tokens": [
    { "Nombre": "kiosko-recepcion", "Token": "...", "Roles": [ "Captura" ] },
    { "Nombre": "soporte",          "Token": "...", "Roles": [ "Consulta" ] }
  ]
}
```

Para generar un token (PowerShell):

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }))
```

**La API no arranca si no hay ningún token configurado.** Es a propósito: es
mejor enterarse al instalar el servicio que cuando el kiosko empiece a recibir
401 en el mostrador, y sobre todo evita que quede abierta a cualquiera en la red
por un descuido de configuración. Por la misma razón, cualquier endpoint que se
agregue después exige token aunque se olvide ponerle `[Authorize]`.

En producción conviene no dejar los tokens en `appsettings.json` dentro del
repositorio, sino en variables de entorno
(`Autenticacion__Tokens__0__Token=...`) o en `dotnet user-secrets`.

## Retención de imágenes

`RetentionCleanupService` corre una vez al día y borra de disco las imágenes
cuyo periodo de retención venció (`Retention:DiasRetencion`, 365 por omisión).
El criterio vive en `RetentionPolicy` y está separado para poder probarlo.

La regla de negocio es "contar desde la última salida", porque un huésped
recurrente reinicia el periodo. Aplicarla al pie de la letra dejaba un hueco:
una persona **sin salida registrada nunca calificaba**, y sus imágenes se
quedaban en disco para siempre. Ahora ese caso es esperable, porque quien
administra `dbo.Estancias` es el PMS: puede haber personas capturadas en el
kiosko a las que nunca se les asignó estancia (un walk-in que se arrepintió) o
estancias que quedaron abiertas por un cierre que no se hizo.

Por eso se toma como referencia **la señal de actividad más reciente**, venga de
donde venga: última salida, última entrada, última modificación del registro o
fecha de alta. Tomar el máximo es lo conservador — nunca borra antes de tiempo —
y a la vez cierra el hueco de las imágenes que no se borraban nunca.

Arranca con el borrado **desactivado** (`HabilitarBorradoAutomatico: false`): el
job solo registra en el log quiénes serían candidatos, para que puedas revisarlo
antes de activarlo. La consulta equivalente en T-SQL, con el mismo criterio,
está en `sql/verificar_migracion.sql`.

## Otras decisiones de diseño

- Las imágenes NUNCA se guardan en la base de datos. Van a disco, con nombre
  calculado a partir del `ID` de la persona (`{ID}_frente.jpg`,
  `{ID}_reverso.jpg`). No se guarda ninguna ruta en `Personas` — ni siquiera se
  usa el campo `Ocupacion`. Se escriben a un archivo temporal y se mueven encima
  del definitivo, para que una subida cortada a la mitad no deje truncada la
  imagen que ya estaba guardada.
- Las fechas de `Estancias` (`FIngreso`, `FSalida`) y `Personas.FechaAlta` están
  en formato `TDateTime` de Delphi (numeric), no en `DATETIME` nativo de SQL
  Server. La conversión correcta vive en `DelphiDateTime` — **no uses
  `CAST(columna AS DATETIME)` directo en T-SQL**, da un resultado desfasado
  2 días. Hay una prueba que fija justamente eso.
- La cadena de conexión usa autenticación integrada de Windows
  (`Trusted_Connection=True`), ya que la API y SQL Server Express corren en la
  misma PC — así no hay contraseña de base de datos que proteger.
- El puerto se configura en `Kestrel:Endpoints` dentro de `appsettings.json`,
  no en `launchSettings.json` (ese archivo no aplica al correr como Windows
  Service). La redirección a HTTPS solo se activa si de verdad hay un endpoint
  HTTPS configurado; si no, Kestrel respondería 307 hacia un puerto donde no
  escucha nadie.

## Cómo correrlo la primera vez

1. Abre `SchId.sln` en Visual Studio (o `code .` si prefieres VS Code con la
   extensión de C#).
2. Edita `src/SchId.Api/appsettings.json`:
   - `ConnectionStrings:SchIdDatabase` con el nombre real de tu instancia de
     SQL Server Express si no es `localhost\SQLEXPRESS`.
   - `ImageStorage:BasePath` con la carpeta donde quieres guardar las
     imágenes (se crea sola si no existe).
   - `Autenticacion:Tokens` con al menos un token (ver arriba). Sin esto la API
     no arranca.
3. `dotnet build` y `dotnet test` desde la raíz del repo.
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

- HTTPS con certificado real o autofirmado para la red local. Mientras tanto el
  token viaja en claro por la red: aceptable en una red cableada y cerrada,
  pero hay que resolverlo antes de meter el kiosko a una wifi compartida.
- Revisar el criterio de retención con quien lleve el tema legal antes de poner
  `HabilitarBorradoAutomatico` en true.
- Empezar el proyecto Android (Kotlin + CameraX + ML Kit) que consume esta API.
- Acordar con el PMS cómo recibe el `Id` que devuelve el registro para amarrar
  la estancia.
