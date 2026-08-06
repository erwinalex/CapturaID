# SchId Kiosko — app Android

Kiosko desatendido que captura la INE del huésped y la manda a la API de SchId.
Kotlin + Jetpack Compose + CameraX + ML Kit.

## Qué hace y qué no

Captura y envía. Nada más:

1. Foto del **frente** de la credencial.
2. Foto del **reverso**, leyendo al mismo tiempo su código de barras.
3. `POST /api/personas/registro` con los datos y las dos imágenes.

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

## Lo que hay que verificar contra una credencial real

**El contenido del PDF417 de la INE no está documentado públicamente** y cambia
entre modelos de credencial. Por eso `LectorIne` no asume posiciones fijas:

- El **CURP se busca por forma**, recorriendo todas las ventanas de 18
  caracteres del contenido y quedándose con la que cumple la estructura oficial
  (y, si hay varias, con la que además cuadra con su dígito verificador). Eso
  funciona sin conocer el formato, **siempre que el CURP venga como texto**.
- El **nombre y la dirección** sí dependen del formato. Se intentan con los
  separadores más comunes y, si no se reconocen, se mandan vacíos — la API
  interpreta un campo vacío como "no se pudo leer" y conserva el valor anterior,
  así que un formato desconocido nunca borra datos buenos.

Antes de producción hay que **escanear una credencial real y confirmar qué trae
el código**. Si el CURP no viene como texto plano, hay que resolverlo por OCR
del frente o por captura manual; ese camino está previsto en el diseño pero no
implementado.

La edad se calcula del propio CURP, deduciendo el siglo de la homoclave (dígito
para nacidos antes del 2000, letra para después).

El dígito verificador del CURP se calcula y se usa **como aviso, no como
rechazo**: si el algoritmo tuviera un detalle mal, un CURP legítimo bloquearía el
mostrador. Cuando no cuadra, la pantalla pide revisar en recepción y el registro
sigue.

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
- Captura manual / OCR como respaldo, para credenciales cuyo código no se pueda
  leer.
- Confirmar el formato real del PDF417 (ver arriba) y ajustar el mapeo de
  nombre y dirección.
- Icono propio: hoy usa el de omisión de Android.
