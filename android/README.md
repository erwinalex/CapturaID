# SCH · ID — app del kiosko

Módulo de identificaciones del **Sistema de Control Hotelero**. Kiosko
desatendido que captura la identificación del huésped —credencial de elector o
pasaporte— y la manda a la API de SchId. Kotlin + Compose + CameraX + ML Kit.

## Qué hace y qué no

Captura y envía. Nada más:

1. El huésped elige **INE** o **pasaporte**.
2. Fotos del documento (frente y reverso para la INE; la página de datos para
   el pasaporte), leyendo códigos al mismo tiempo.
3. `POST /api/personas/registro` con los datos y las imágenes.

**No consulta si el huésped ya existe, no muestra datos y no asigna estancias.**
Esas tres cosas son deliberadas:

- El alta o la actualización la resuelve la API: manda todo y ella decide si crea
  el registro o actualiza el que ya había. Así el kiosko no necesita pedir el
  registro previo, que es lo que obligaría a mandarle datos del huésped a una
  tableta que está en un mostrador.
- La pantalla nunca muestra nombre, CURP ni dirección: solo instrucciones y el
  desenlace. Cualquiera que pase junto al kiosko alcanza a leerla.
- Las estancias las lleva el PMS.

## Privacidad, que es lo que dicta el diseño

- **Las fotos nunca tocan el disco.** `ImageCapture` se usa en su variante en
  memoria; los bytes se mandan y se sobrescriben con ceros. No hay archivos
  temporales ni nada en la galería.
- **`FLAG_SECURE`** bloquea capturas de pantalla, grabación y la miniatura que
  Android guarda de la app en la lista de recientes — esa miniatura sería una
  copia de la INE conservada por el sistema sin que nadie la pidiera.
- **Sin respaldos.** `allowBackup=false` y reglas de extracción que excluyen
  todo: el token no tiene por qué acabar en una copia en la nube.
- **El contenido del código de barras no se guarda ni se registra en el log.**
  Cuando no se reconoce un CURP, el error solo lleva el largo del contenido.
- Al terminar cada captura se borra todo de memoria, haya funcionado o no. Si
  el envío falla, el huésped repite; guardar sus fotos "por si acaso" sería
  acumular imágenes de INE en el dispositivo.

## Cómo lee los documentos

La lectura baja por tres escalones y solo pasa al siguiente si el anterior no
dio una identidad utilizable. En el caso normal el huésped ni se entera de que
existen.

### 1. Código de barras — **QR primero, PDF417 después**

Es lo más confiable cuando funciona: no hay interpretación de por medio. Se
prefiere el QR porque en los modelos de credencial que traen los dos es el más
nuevo y el que se lee con menos reintentos; un QR reconocido reemplaza a un
PDF417 que ya se había leído.

### 2. OCR de las fotos que ya se tomaron

Entra cuando el código está rayado, borroso o el modelo no lo trae legible.
**No se le pide nada nuevo al huésped**: corre sobre las mismas imágenes que ya
se capturaron, primero el reverso y luego el frente.

### 3. Captura manual

Último recurso, para recepción. Es la única pantalla del kiosko que muestra
datos, porque alguien tiene que teclearlos; aun así no trae nada del registro
previo y no deja nada al terminar. Aquí sí se exige que el CURP tenga la
estructura correcta: un CURP mal tecleado crea un huésped duplicado que nadie va
a notar.

## Lo que hay que verificar contra documentos reales

**El contenido del QR y del PDF417 de la INE no está documentado públicamente** y
cambia entre modelos de credencial. Por eso el lector no asume posiciones fijas:

- El **CURP se busca por forma**, recorriendo todas las ventanas de 18
  caracteres del contenido y quedándose con la que cumple la estructura oficial
  (y, si hay varias, con la que además cuadra con su dígito verificador). Eso
  funciona sin conocer el formato, **siempre que el CURP venga como texto**. La
  misma búsqueda sirve para el OCR, porque el CURP viene impreso en el frente.
- El **nombre y la dirección** sí dependen del formato. Se intentan con los
  separadores más comunes y, si no se reconocen, se mandan vacíos — la API
  interpreta un campo vacío como "no se pudo leer" y conserva el valor anterior,
  así que un formato desconocido nunca borra datos buenos.

Antes de producción hay que **escanear una credencial real y confirmar qué trae
el código**. Si no viniera nada aprovechable, la cadena cae sola en OCR y, si
acaso, en captura manual — que es justo para lo que están.

La edad se calcula del propio CURP, deduciendo el siglo de la homoclave (dígito
para nacidos antes del 2000, letra para después).

El dígito verificador del CURP se calcula y se usa **como aviso, no como
rechazo**: si el algoritmo tuviera un detalle mal, un CURP legítimo bloquearía el
mostrador. Cuando no cuadra, la pantalla pide revisar en recepción y el registro
sigue.

## Pasaportes

El caso opuesto al de la INE: la **MRZ** (las dos líneas de 44 caracteres al pie
de la página de datos) **sí está especificada públicamente**, en la norma ICAO
9303. Se lee por posiciones fijas con confianza, y además trae dígitos de
control, así que un OCR mal leído se detecta antes de mandar basura al servidor
— algo que con la INE no se puede hacer.

Las pruebas usan el espécimen de la propia norma como vector de verificación:
sus dígitos de control son los correctos por definición, así que si no validan,
el error es nuestro.

### La llave de identidad

Un pasaporte no trae CURP, y la API usa ese campo como llave para no duplicar
huéspedes. Se arma entonces una clave determinista:

```
PAS-<país emisor>-<número de pasaporte>     ej. PAS-USA-123456789
```

El mismo documento produce siempre la misma clave, así que un extranjero
recurrente se actualiza en vez de duplicarse. El prefijo hace evidente en la
base de datos que ese registro no es un CURP.

**Esta decisión conviene revisarla antes de producción**: mete claves que no son
CURP en la columna `Personas.CURP`, y si el PMS asume que ahí siempre hay un
CURP válido, hay que avisarle. La alternativa sería agregar una columna de tipo
de documento, que es un cambio de esquema. Cabe de sobra: la clave más larga
mide 16 caracteres y la columna es `nchar(20)`.

## Compilar

Necesita JDK 17+ y el Android SDK (API 35).

```bash
cd android
./gradlew assembleDebug      # APK en app/build/outputs/apk/debug/
./gradlew testDebugUnitTest  # pruebas del parser
```

`local.properties` con `sdk.dir=` apuntando a tu SDK no se versiona; Android
Studio lo genera solo al abrir el proyecto.

## Instalar un kiosko

1. **Certificado.** Copia el `schid_ca.crt` que generó
   `scripts/New-SchIdCertificado.ps1` sobre
   `app/src/main/res/raw/schid_ca.crt`, reemplazando el marcador que trae el
   repositorio, y recompila.

   El archivo versionado es un **marcador de posición** que existe nada más para
   que el proyecto compile. Si se te olvida reemplazarlo, la app no conecta y
   falla con error de certificado — que es la forma correcta de fallar.

2. **Dominio.** En `res/xml/network_security_config.xml`, cambia
   `schid-servidor` por la IP o el nombre real del servidor. Tiene que coincidir
   **exactamente** con lo que quedó en el SAN del certificado y con la URL que se
   configure en la app.

3. **Instala el APK** y ábrelo. En *Ajustes* (PIN de fábrica `0000`) captura:
   - Dirección del servidor, con `https://` — la app rechaza `http://`, porque
     el token viajaría en claro.
   - El token del kiosko (rol `Captura`).
   - **Un PIN nuevo.** La pantalla avisa mientras siga el de fábrica.

4. **Ancla la app a la pantalla.** La app llama a `startLockTask()` al abrirse.
   Sin un MDM, Android pide confirmación la primera vez. Para que quede anclada
   de forma permanente hay que ponerla como *device owner*:

   ```bash
   adb shell dpm set-device-owner mx.schid.kiosko/.AdminReceptor
   ```

   Eso requiere un `DeviceAdminReceiver`, que **todavía no está implementado**:
   por ahora el anclaje es el de Android con confirmación del usuario. La app
   también declara la categoría `HOME`, así que puedes elegirla como lanzador
   para que el botón de inicio no saque al huésped al escritorio.

## Pendientes

- `DeviceAdminReceiver` para poder dejarla como device owner y anclarla sin
  confirmación.
- Confirmar el formato real del QR y del PDF417 (ver arriba) y ajustar el mapeo
  de nombre y dirección.
- Decidir con el PMS si le sirve la clave sintética de pasaporte o hace falta
  una columna de tipo de documento.
- Las heurísticas de OCR para el nombre y el domicilio de la INE se basan en las
  etiquetas impresas ("NOMBRE", "DOMICILIO"); habrá que afinarlas contra
  credenciales reales de cada modelo.
