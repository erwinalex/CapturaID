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
- `scripts/New-SchIdCertificado.ps1` — genera la CA y el certificado del
  servidor para HTTPS en la red local.
- `android/` — la app del kiosko (Kotlin + Compose + CameraX + ML Kit). Tiene su
  propio README con el detalle de instalación y las decisiones de privacidad.

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

## HTTPS en la red local

El token del kiosko viaja en un encabezado HTTP. Sin TLS va en claro y
cualquiera que escuche la red se lo queda, así que HTTPS no es opcional una vez
que el kiosko está en operación. La API arranca igual en HTTP —hace falta para
instalar y diagnosticar— pero lo deja dicho en el log con un warning.

### 1. Generar los certificados

Hay dos modos. Para un despliegue de decenas de ubicaciones, el que sirve es el
**compartido**.

#### Modo compartido: un certificado para las 70 ubicaciones (recomendado)

Se corre **una sola vez**, en cualquier PC con Windows, en PowerShell **como
administrador**:

```powershell
cd scripts
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\New-SchIdCertificado.ps1 -Compartido
```

Windows viene con la ejecución de scripts deshabilitada, de ahí el
`Set-ExecutionPolicy`. Con `-Scope Process` el permiso muere al cerrar la
ventana, así que no deja la máquina permanentemente más abierta por algo que se
hace una vez.

El certificado lleva en el SAN **únicamente el nombre `schid-servidor`**, sin
ninguna IP, y se exporta como `schid_servidor.pfx` protegido con la contraseña
que el script pida.

En **cada una de las demás ubicaciones** se copian el `.pfx`, el `schid_ca.crt`
y el propio script, y se corre:

```powershell
.\New-SchIdCertificado.ps1 -Instalar -ArchivoPfx .\schid_servidor.pfx -ArchivoCa .\schid_ca.crt
```

Ese modo no genera nada: importa el certificado al almacén de la máquina y **la
CA al almacén de raíces de confianza**. Los dos pasos hacen falta — sin la CA en
las raíces, esa máquina no confía en su propia cadena y Kestrel, que carga con
`AllowInvalid: false`, ni siquiera encuentra el certificado al buscarlo por
nombre. El error que verías sería "no hay certificado", no "certificado
inválido", que es de los que cuestan una tarde.

Funciona porque la app **nunca conecta por IP**: pide siempre
`https://schid-servidor:puerto` y resuelve ese nombre por su cuenta hacia la IP
que tenga configurada el kiosko. De ahí salen las tres cosas que hacían falta:

- Un certificado sirve para todas las ubicaciones.
- Una sola compilación del APK sirve para todos los kioskos.
- Donde el router reparte IP dinámica, un cambio de dirección se arregla en los
  ajustes del kiosko — sin regenerar certificados ni recompilar.

> El `.pfx` lleva la llave privada del servidor. Trátalo como una contraseña:
> pásalo por un medio seguro y borra las copias en cuanto esté instalado.

#### Modo por ubicación

Sigue disponible para una instalación suelta donde el servidor tiene IP fija y
no interesa distribuir un `.pfx`:

```powershell
.\New-SchIdCertificado.ps1 -Direcciones 192.168.1.226,192.168.1.250,schid-servidor
```

Aquí el certificado se queda en el almacén de Windows de esa máquina y no se
exporta. Pon en `-Direcciones` **todas** las formas en que los kioskos van a
alcanzar al servidor, en una sola lista separada por comas. Si alguna se queda
suelta fuera de la lista, el script rechaza el comando en lugar de generar en
silencio un certificado incompleto. Eso es lo que acaba en el SAN, y **Android
valida el SAN e ignora el CN**: si el kiosko apunta a `https://192.168.1.50` y
esa IP no está ahí, la conexión falla aunque el certificado sea perfectamente
válido. Las IPs y los nombres DNS van en campos distintos del SAN; el script los
separa solo, pero una IP no sirve si quedó registrada como nombre.

#### En los dos modos

El script crea **una CA propia y, firmado por ella, el certificado del
servidor**. Suena a rodeo frente a un autofirmado suelto, pero es lo que hace
manejable la renovación: en el kiosko se declara de confianza la CA, no el
certificado del servidor. Con una CA de 10 años y certificados de servidor de 2,
renovar es volver a correr el script — sin tocar ni reinstalar la app en cada
kiosko. Con un autofirmado, cada renovación obliga a actualizar todos los
dispositivos.

Si corres el script otra vez y la CA sigue vigente, la reutiliza en lugar de
crear una nueva, justamente para no invalidar a los kioskos que ya confían en
ella.

### 2. Configurar Kestrel

El script imprime el bloque exacto para `src/SchId.Api/appsettings.json`:

```json
"Kestrel": {
  "Endpoints": {
    "Https": {
      "Url": "https://0.0.0.0:7443",
      "Certificate": {
        "Subject": "schid-servidor",
        "Store": "My",
        "Location": "LocalMachine",
        "AllowInvalid": false
      }
    }
  }
}
```

El certificado se lee del almacén de Windows por su nombre, no de un archivo
`.pfx` en disco. Así no queda una contraseña de certificado guardada en la
configuración, por la misma razón que la cadena de conexión usa autenticación
integrada en vez de una contraseña de base de datos. En modo compartido el
`.pfx` es solo el vehículo para llevar el certificado a cada servidor:
`Import-PfxCertificate` lo mete al almacén y a partir de ahí sobra — bórralo.

El bloque es **idéntico en las 70 ubicaciones**: el nombre del certificado es el
mismo en todas.

**Quita la sección `Http` cuando los kioskos ya estén en HTTPS.** Mientras siga
ahí, un kiosko mal configurado puede seguir mandando su token en claro sin que
nadie lo note; la API avisa de esto en el log al arrancar. Si dejas ambos, el
redirect a HTTPS se activa solo (y solo entonces: activarlo sin un endpoint
HTTPS configurado hacía que Kestrel respondiera 307 hacia un puerto donde no
escucha nadie).

Abre el puerto en el firewall y reinicia el servicio:

```powershell
New-NetFirewallRule -DisplayName "SchId API (HTTPS)" -Direction Inbound -Protocol TCP -LocalPort 7443 -Action Allow
Restart-Service SchIdApi
```

### 3. Confiar en la CA desde el kiosko

Copia `schid_ca.crt` (lo deja el script en `C:\SchId\certificados`) al proyecto
Android en `app/src/main/res/raw/schid_ca.crt` y recompila. Ese archivo **no
está versionado** a propósito, para no tener que reponerlo en cada actualización
del repositorio; ver `android/certificado/LEEME.md`. El
`network_security_config.xml` de la app ya viene con la CA declarada y explica
por qué confía **solo** en la nuestra para el nombre del servidor —para que
ninguna CA pública comprometida pueda meterse en medio— mientras conserva las
CAs del sistema para todo lo demás.

No hace falta instalar nada a mano en el dispositivo ni pedir permisos de
administrador en la tableta, y **ese archivo no se edita por ubicación**: el
nombre `schid-servidor` es el mismo en todas, y lo que cambia de una a otra —la
IP— se captura en los ajustes del kiosko. Ver `android/README.md`.

### Si el kiosko dice que el servidor cerró la conexión

Es el síntoma más desconcertante: la app falla al instante y en el servidor no
aparece nada. **Un saludo TLS que el servidor corta no genera ninguna petición**,
así que no hay nada que registrar en el log de acceso.

Ojo con la diferencia, porque mandan a revisar lados opuestos:

| Lo que dice la app | Dónde está el problema |
|---|---|
| "trust anchor ... not found" | En la app: le falta el `schid_ca.crt` correcto |
| "connection closed" / "reset" | En el servidor: su certificado no es utilizable |

Ninguno de los dos logs dice **quién** cortó el saludo, así que los dos lados se
señalan mutuamente. Lo que sí lo dice es el log del servicio al arrancar: la API
reporta qué certificado encontró, su huella, cuándo vence, las direcciones de su
SAN y las dos cosas que de verdad importan.

```
HTTPS: usando el certificado CN=schid-servidor, huella A1B2..., vence 2028-08-13.
HTTPS: direcciones del certificado (SAN): DNS Name=schid-servidor
HTTPS: la llave privada del certificado es utilizable por esta cuenta (SERVICIO).
```

**1. Que la llave privada se pueda usar de verdad.** No basta con que el
certificado la tenga: si vive en el almacén de la máquina y la cuenta que corre
el servicio no tiene permiso sobre ella, el certificado carga —la parte pública
siempre se puede leer— y el saludo TLS falla igual. Por eso el arranque **firma
unos bytes de prueba**, que es lo mismo que hace TLS. Si no puede:

```
HTTPS: el certificado tiene llave privada pero esta cuenta (Erwin) NO puede usarla
```

Se arregla con `certlm.msc`: botón derecho sobre el certificado, *Todas las
tareas*, *Administrar claves privadas*, y agregar la cuenta. Si el servicio corre
con una cuenta concreta, pásasela al script con `-CuentaServicio` y lo hace solo.
Si lo levantas en consola, prueba con una consola de administrador.

**2. Que esta máquina confíe en su propia CA.** Kestrel carga con
`AllowInvalid: false`, así que si la CA no está en el almacén de raíces el
certificado no es que sea inválido: **no aparece**, y el error habla de un
certificado ausente, que manda a buscar donde no es.

```
HTTPS: esta máquina no confía en la CA que firmó el certificado
```

Pasa al instalar el certificado compartido en una ubicación donde se importó el
`.pfx` pero se olvidó la CA. Lo arregla `-Instalar`, que hace las dos cosas.

**3. Que el servidor pueda completar un saludo TLS.** Al arrancar, el servicio
abre una conexión TLS **contra sí mismo**. Es lo que corta la discusión de quién
tiene la culpa, porque el síntoma se ve idéntico desde los dos lados:

```
HTTPS: saludo TLS contra sí mismo completado en el puerto 7443. El certificado y su llave privada sirven.
HTTPS: lo firmó la CA CN=SchId CA Local, huella 3F9C...
HTTPS: si el kiosko sigue fallando con este servidor en verde, lo que falla es la app.
```

Si esa línea sale, **el servidor está bien** y lo que falla es el kiosko: casi
siempre su `schid_ca.crt` no corresponde a esa CA. Compara la huella de arriba
con la del archivo compilado en el APK:

```powershell
certutil -dump android\app\src\main\res\raw\schid_ca.crt | findstr /i "hash huella"
```

Si en cambio dice `NO pudo completar un saludo TLS consigo mismo`, el problema
está en el servidor y ahí viene la excepción.

### Si el arranque no reporta nada raro

Entonces el que cortó fue el cliente, y hay que ver el motivo exacto que Kestrel
sí conoce pero no registra al nivel por omisión. En `appsettings.json`:

```json
"Microsoft.AspNetCore.Server.Kestrel": "Debug"
```

Reinicia, reproduce el fallo desde el kiosko y busca "Failed to authenticate
HTTPS connection": ahí viene la excepción real. No lo dejes en `Debug` en
operación normal, que llena el log.

Lo más común en ese escenario es que la app y el servidor no tengan la misma CA
—se regeneró de un lado y no del otro—. Compara la huella de la CA del servidor
con la del `schid_ca.crt` que se compiló en el APK.

### Ver qué está pasando en cada conexión

El servicio registra cada conexión TCP y cada petición HTTP:

```
[conexion] abierta desde 192.168.1.55:41234
[peticion] POST /api/personas/registro desde 192.168.1.55 [kiosko-recepcion] -> 200 en 340 ms
[conexion] cerrada 192.168.1.55:41234 tras 380 ms, 1 petición(es).
```

Lo que hace útil este par es **contar las peticiones de cada conexión**. Cuando
el saludo TLS falla, la conexión se abre y se cierra sin llegar a producir
ninguna petición, así que el log de peticiones no tiene nada que mostrar y desde
el servidor parece que el cliente nunca llamó. Ese caso ahora se distingue solo:

```
[conexion] cerrada 192.168.1.55:41240 tras 28 ms, SIN peticiones HTTP.
```

Los códigos vienen explicados —`401` sin token, `403` rol insuficiente— y se
registra con qué token entró cada petición, que es lo que permite ver de un
vistazo si un kiosko está usando el equivocado. **El CURP no se escribe en el
log**: la ruta de la consulta se guarda como `/api/personas/curp/***`.

Para el motivo exacto de un saludo fallido, sube el nivel de Kestrel
temporalmente en `appsettings.json`:

```json
"Microsoft.AspNetCore.Server.Kestrel": "Debug"
```

Es ruidoso para el día a día —registra cada lectura y escritura— así que
conviene volverlo a bajar cuando termines. Y si el registro de conexiones
estorba, se apaga con `"Diagnostico": { "RegistrarConexiones": false }`.

### Verificar que quedó bien

Desde la PC del servidor:

```powershell
curl.exe -v https://192.168.1.50:7443/api/personas/curp/PRUEBA -H "X-Api-Key: <token de consulta>"
```

Un `401` o un `403` ya son buena señal: significa que el TLS se estableció y la
petición llegó a la autenticación. Lo que no debe aparecer es un error de
certificado.

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
4. Antes de tocar datos reales, corre la migración. **Por omisión solo simula**,
   así que este comando no modifica nada:

   ```
   cd src/SchId.ImageMigration
   dotnet run -- "Server=localhost\SQLEXPRESS;Database=SCHIDData;Trusted_Connection=True;TrustServerCertificate=True;" "C:\SchId\ImagenesINE"
   ```

   Revisa la salida: te dice cuántas personas tienen imágenes, qué archivos
   escribiría y de qué tamaño.

   Ojo con el nombre de la instancia: en la línea de comandos de Windows la
   barra invertida **no se escapa**, así que va sencilla (`localhost\SQLEXPRESS`).
   Si escribes dos, se mandan las dos y no encuentra la instancia.

5. **Haz un backup completo de `SCHIDData` antes del siguiente paso.**
6. Corre la migración real agregando **`--ejecutar`**. Al terminar, sigue la
   instrucción que imprime en pantalla para correr `DBCC SHRINKFILE` en SSMS
   y recuperar el espacio en disco.

   El borrado se pide explícitamente a propósito: la herramienta vacía
   `IDFoto1` e `IDFoto2`, y con ese nivel de daño lo que se olvida escribir no
   debería ser el freno. Se puede volver a correr sin problema — las personas
   cuyas columnas ya quedaron en NULL no se vuelven a procesar.
7. Corre `sql/verificar_migracion.sql` antes y después para confirmar el
   espacio liberado y que ya no queden imágenes en la tabla.

## Cómo instalarlo como servicio de Windows

```
dotnet publish src/SchId.Api -c Release -o C:\SchId\Api --self-contained false
sc create SchIdApi binPath= "C:\SchId\Api\SchId.Api.exe"
sc start SchIdApi
```

## Pendientes / siguiente paso

- Revisar el criterio de retención con quien lleve el tema legal antes de poner
  `HabilitarBorradoAutomatico` en true.
- Anotar en el calendario la fecha de vencimiento del certificado del servidor
  (la imprime el script al terminar). Si vence, los kioskos dejan de conectar.
- Confirmar contra una credencial real qué trae el código de barras del reverso.
  Es el único supuesto del kiosko que no se pudo verificar sin tener una INE a
  la mano; está explicado en `android/README.md`.
- **Decidir cómo se identifica a un huésped extranjero.** El kiosko ya lee
  pasaportes por su MRZ, pero un pasaporte no trae CURP y esta API usa ese campo
  como llave. Por ahora se guarda una clave sintética y determinista
  (`PAS-USA-123456789`) en `Personas.CURP`, con prefijo para que se note que no
  es un CURP. Funciona y no duplica huéspedes recurrentes, pero si el PMS asume
  que esa columna siempre trae un CURP válido, hay que avisarle o agregar una
  columna de tipo de documento.
- Empezar el proyecto Android (Kotlin + CameraX + ML Kit) que consume esta API.
- Acordar con el PMS cómo recibe el `Id` que devuelve el registro para amarrar
  la estancia.
