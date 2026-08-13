package mx.schid.kiosko.config

/**
 * Lo que el flujo de captura necesita saber de la configuración del kiosko.
 *
 * Se separa de [ConfiguracionKiosko] —que depende de un Context y del almacén
 * cifrado— para poder probar el flujo con una implementación de mentira.
 */
interface AjustesServidor {
    /** IP o nombre del servidor de esta ubicación, con puerto opcional. */
    val direccionServidor: String
    val token: String
    val estaConfigurado: Boolean
}
