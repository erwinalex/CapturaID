# SCH · ID — app del kiosko

Módulo de identificaciones del **Sistema de Control Hotelero**. Kiosko
desatendido que captura la identificación del huésped —credencial de elector o
pasaporte— y la manda a la API de SchId. Kotlin + Compose + CameraX + ML Kit.

## Qué hace y qué no

Captura y envía. Nada más:

1. El huésped elige **INE** o **pasaporte**.
2. Fotos del documento (frente y reverso para la INE; la página de datos para
   el pasaporte). La del reverso de la INE es para el expediente, no para leer:
   ahí no hay nada que la app pueda aprovechar.
3. **Confirmación**: el huésped revisa lo que se leyó y lo corrige si hace falta.
4. `POST /api/personas/registro` con los datos y las imágenes.

**No consulta si el huésped ya existe, no muestra datos y no asigna estancias.**
Esas tres cosas son deliberadas:

- El alta o la actualización la resuelve la API: manda todo y ella decide si crea
  el registro o actualiza el que ya había. Así el kiosko no necesita pedir el
  registro previo, que es lo que obligaría a mandarle datos del huésped a una
  tableta que está en un mostrador.
- Fuera de la pantalla de confirmación, no se muestra nombre, CURP ni
  dirección: solo instrucciones y el desenlace. Y lo que la confirmación
  enseña son **los datos que acaban de salir del documento que el huésped
  tiene en la mano**, nunca el registro previo que hubiera en la base.
- Las estancias las lleva el PMS.

## El recorte a la credencial

La cámara ve todo: el mostrador, la mano, lo que haya detrás. Según el formato
del sensor puede acabar habiendo más fondo que documento, y eso significa más
espacio del necesario, un OCR con más ruido del necesario, y datos del entorno
que nadie pidió conservar.

En la pantalla de captura hay una **guía con la proporción de una credencial**
(ID-1 de la norma ISO/IEC 7810: 85.60 × 53.98 mm, la misma de una tarjeta
bancaria). Lo que queda fuera se ve oscurecido, y **solo se guarda lo de
adentro**.

La geometría sale de `RecorteCredencial`, y de ahí salen las dos cosas: el
rectángulo que se dibuja y el recorte que se aplica a la foto. Son el mismo
cálculo a propósito — con dos números separados, cualquier ajuste en uno dejaría
al otro mintiendo sobre lo que se va a guardar.

Para que la guía corresponda con lo que se captura, la vista previa y la captura
comparten proporción (4:3) y la vista se ajusta sin recortar. La caja que las
contiene tiene esa misma proporción, así que no hay franjas ni desplazamientos
entre lo que se ve y lo que se guarda.

La guía deja margen a los lados a propósito: el huésped acomoda el documento a
ojo, y es preferible guardar un poco de fondo a cortarle el borde a la
credencial.

Si el recorte falla por lo que sea, se guarda la foto completa. Es una mejora,
no un requisito: mejor de más que quedarse sin imagen.

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

Es lo más confiable cuando funciona: no hay interpretación de por medio.

**En la INE, este escalón no aporta nada.** Se comprobó con credenciales reales:

- El **QR pequeño** del reverso solo lleva un enlace al sitio del INE, sin datos
  del titular.
- Los **dos códigos grandes** no los decodifica ML Kit ni buscando todos los
  formatos. Según el propio INE llevan los datos de la persona más sus
  **biométricos cifrados**, así que aunque se decodificaran no habría nada
  utilizable: la llave la tiene el INE. Y aunque se pudiera, guardar biométricos
  de huéspedes es una categoría legal mucho más pesada que archivar la foto de
  una identificación — no es terreno donde convenga meterse para ahorrarse un
  OCR.

El escáner se mantiene porque ya está corriendo para la vista previa y no cuesta
nada, y porque un pasaporte u otro modelo de credencial sí podrían traer algo.
Pero **para la INE la vía es el OCR**, no un respaldo.

### 2. OCR de las fotos que ya se tomaron

**Es el camino que de verdad funciona con la INE.** No se le pide nada nuevo al
huésped: corre sobre las mismas imágenes que ya se capturaron, empezando por el
**frente**, que es donde la credencial imprime nombre, domicilio y CURP.

El CURP se busca anclado a su etiqueta: en todas las credenciales la palabra
`CURP` va justo antes del dato, y lo mismo `NOMBRE` y `DOMICILIO`. El ancla es
más fiable que buscar por forma, y sobre todo evita confundir el CURP con la
**clave de elector**, que también son 18 caracteres alfanuméricos y aparece
antes en el texto.

### La corrección de lecturas del OCR

Sobre una credencial real el OCR devolvió `...HDFRSRO5`: una letra **O** donde
va un **cero**. Y **el dígito de control no lo detecta** — la diferencia entre
`O` (25) y `0`, por el peso 2 de esa posición, es múltiplo de 10. Sin corregirlo,
un CURP equivocado se manda al servidor con toda apariencia de estar bien, y el
día que el OCR lo lea correctamente aparece un huésped duplicado que nadie
relaciona con el primero.

La estructura del CURP dice qué posiciones son forzosamente dígitos (la fecha de
nacimiento y el dígito de control) y cuáles forzosamente letras (iniciales,
sexo, entidad, consonantes). Cada carácter fuera de sitio se corrige con esa
regla, no adivinando. La homoclave se resuelve aparte: es un dígito para quienes
nacieron antes del 2000, y si leer el año como `20AA` cayera en el futuro, la
persona nació en el siglo pasado.

Si ni corrigiendo se sostiene la estructura, no se inventa nada: se cae a la
búsqueda por forma y, de ahí, a captura manual.

La edad se toma de la **fecha impresa** en la credencial, no de la que se deduce
del CURP: viene con el año completo y así no hay que adivinar el siglo a partir
de la homoclave, que es justo el carácter que más se confunde.

### 3. Repetir la foto, y solo entonces captura manual

Cuando no se leyó nada, lo primero que se ofrece es **volver a tomar la foto**.
Casi siempre la causa es un reflejo o un desenfoque, y repetir cuesta segundos
frente a teclear un CURP completo. La captura manual queda como la segunda
opción de esa misma pantalla, siempre disponible.

Esto pesa más ahora que se sabe que el código de barras no aporta nada en la
INE: el OCR no es un respaldo, es *la* vía, y conviene darle un segundo intento
barato antes de mandar a alguien a teclear.

### Y siempre, la confirmación

**Nada se manda al servidor sin pasar por `PantallaDatos`.** Es el mismo
formulario en los dos casos: llega lleno con lo que se leyó (confirmación) o
vacío (captura manual). El huésped revisa, corrige lo que no coincida con su
documento y confirma.

Aquí sí se exige que el CURP tenga la estructura correcta: uno mal escrito crea
un huésped duplicado que nadie va a notar. El dígito de control, en cambio,
solo pinta un aviso.

Se conserva de dónde vino la lectura aunque el huésped corrija un campo: sigue
siendo útil saber que el dato salió de un QR y no de una captura a mano.

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

Para eso está el **modo diagnóstico**, en *Ajustes → Diagnóstico de lectura*:
enseña tal cual el contenido del código y el texto que reconoce el OCR, sin
interpretarlos, y dice si encuentra un CURP en cada uno. Con eso se puede ver
qué trae de verdad una credencial concreta y afinar el mapeo de nombre y
domicilio. Muestra datos en claro, así que **es para configurar con un documento
de prueba, no para el mostrador**; nada de lo que aparece ahí se guarda ni se
manda a ningún lado.

El CURP se busca dos veces: sobre el texto tal cual, y —si ahí no sale— sobre el
texto compactado sin espacios ni saltos de línea. El OCR parte seguido el CURP
en dos pedazos, y sin ese segundo intento una INE real no se reconocería.

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

## Dónde se edita la interfaz

No hay diseñador visual de arrastrar y soltar: la interfaz está hecha con
**Jetpack Compose**, o sea declarada en código Kotlin. El *Layout Editor* de
Android Studio pertenece al enfoque anterior, basado en XML, y este proyecto no
usa XML de layouts.

| Archivo | Qué dibuja |
|---|---|
| `ui/PantallaCaptura.kt` | Inicio, elección de documento, cámara, "Listo", errores |
| `ui/PantallaCapturaManual.kt` | Formulario de captura manual |
| `ui/PantallaConfiguracion.kt` | PIN y ajustes (URL, token) |

Para verlas sin instalar nada en la tableta, cada archivo trae funciones
anotadas con `@Preview` al final. Android Studio las dibuja al lado del código;
con el botón **Split** (arriba a la derecha del editor) se ven código y
resultado en paralelo, y se actualiza conforme escribes.

La pantalla de la cámara no se previsualiza: CameraX necesita hardware, así que
ese paso hay que verlo en el dispositivo.

## Compilar

Necesita JDK 17+ y el Android SDK (API 35). Android Studio trae su propio JDK y
el proyecto incluye el wrapper de Gradle, así que no hace falta instalarlos
aparte. **Abre la carpeta `android/`, no la raíz del repositorio**: la raíz
tiene la solución de .NET y ningún `settings.gradle`.

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

3. **Instala el APK** y ábrelo. En *Ajustes* (PIN de fábrica **`0000`**)
   captura:
   - Dirección del servidor, **con el puerto incluido en la URL**
     (`https://192.168.1.50:7443`).
   - El token del kiosko (rol `Captura`).
   - **Un PIN nuevo.** La pantalla avisa mientras siga el de fábrica.

### Probar sin certificado (http)

La compilación de **depuración** acepta `http://`, para poder probar contra un
servidor que todavía no tiene certificado. Son dos piezas que van juntas:

- `ValidadorUrl` permite `http://` solo cuando `BuildConfig.DEBUG` es cierto.
- `src/debug/res/xml/network_security_config.xml` reemplaza al de `src/main`
  durante `assembleDebug` y habilita el tráfico sin cifrar.

El APK de **release** rechaza `http://` en ambos niveles: al capturar la
dirección y en la red. Así una prueba no se te cuela a producción por descuido.
Mientras uses http, la pantalla de ajustes te lo recuerda — y ten presente que
el token del kiosko viaja en claro.

4. **Prueba la conexión** desde la misma pantalla de Ajustes, con el botón
   *Probar conexión*. Pide un endpoint que el token del kiosko no tiene
   permitido, así que un **403 es la mejor respuesta posible**: demuestra que el
   TLS se estableció, que el servidor recibió la petición y que reconoció el
   token.

   Si algo falla, ahí sí sale el motivo técnico. Los dos habituales:

   - **"El certificado del servidor no es de confianza"** — el `schid_ca.crt`
     que tiene la app no corresponde al que firmó el certificado del servidor,
     o la IP con la que conectas no está en el SAN. Un handshake rechazado
     falla al instante y **no deja rastro en el log del servidor**, porque la
     conexión se corta antes de que llegue ninguna petición; por eso conviene
     mirar aquí y no allá.
   - **"No se pudo conectar"** — IP, puerto, servicio detenido o el firewall de
     Windows bloqueando.

5. **Ancla la app a la pantalla.** La app llama a `startLockTask()` al abrirse.
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
