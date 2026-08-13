# El certificado de la CA

La app confía **únicamente** en nuestra CA para hablar con el servidor de SchId
(ver `app/src/main/res/xml/network_security_config.xml`). El certificado de esa
CA tiene que estar en `app/src/main/res/raw/schid_ca.crt` al compilar.

**Ese archivo no está en el control de versiones.** Antes sí estaba, con un
marcador de posición dentro, y eso obligaba a volver a copiar el certificado
real cada vez que se bajaba código del repositorio — o peor, a subirlo por
accidente en un commit de otra cosa.

## Cómo poner el certificado real

Copia sobre `app/src/main/res/raw/schid_ca.crt` el `schid_ca.crt` que generó
`scripts/New-SchIdCertificado.ps1` (lo deja en `C:\SchId\certificados`), y
recompila. Git ya no lo va a ver, así que se queda ahí entre actualizaciones.

Es el certificado **público** de la CA: no lleva llave privada y no hay problema
en copiarlo por donde sea. La llave privada vive en el `.pfx` del servidor, que
es otra cosa y sí es secreta.

## Qué pasa si no lo pones

`schid_ca_marcador.crt` es un marcador de posición: un certificado real en
formato PEM —para que el recurso exista y el proyecto compile— pero emitido a
nombre de "REEMPLAZAR ESTE ARCHIVO" y **vencido a propósito**.

Gradle lo copia solo cuando `app/src/main/res/raw/schid_ca.crt` no existe, de
modo que un clon recién bajado compila sin pasos previos. Entonces:

- `assembleDebug` compila y avisa en la consola. La app no va a poder conectar
  por https contra el servidor real, que es la forma correcta de fallar: es
  preferible a confiar en cualquier CA.
- `assembleRelease` **falla**. Un APK de kiosko con el marcador dentro no sirve
  para nada, y es mejor enterarse al compilar que en la ubicación.
