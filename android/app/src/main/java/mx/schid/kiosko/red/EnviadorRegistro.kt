package mx.schid.kiosko.red

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import mx.schid.kiosko.config.AjustesServidor
import mx.schid.kiosko.config.DireccionServidor
import mx.schid.kiosko.datos.DocumentoCapturado

/**
 * Manda un registro al servidor.
 *
 * Existe como interfaz para que el flujo de captura se pueda probar sin red:
 * es la única parte del ViewModel que sale del dispositivo, y el resto —que es
 * donde vive la lógica que se puede romper— queda cubierto por pruebas normales.
 */
fun interface EnviadorRegistro {
    suspend fun enviar(
        documento: DocumentoCapturado,
        imagenFrente: ByteArray?,
        imagenReverso: ByteArray?
    ): ResultadoEnvio
}

/**
 * El envío de verdad. Lee la dirección y el token en el momento de mandar, no
 * al construirse, para que un cambio en los ajustes surta efecto en la siguiente
 * captura sin reiniciar la app.
 */
class EnviadorHttp(private val ajustes: AjustesServidor) : EnviadorRegistro {

    override suspend fun enviar(
        documento: DocumentoCapturado,
        imagenFrente: ByteArray?,
        imagenReverso: ByteArray?
    ): ResultadoEnvio = withContext(Dispatchers.IO) {
        val destino = DireccionServidor.interpretar(ajustes.direccionServidor)
            ?: return@withContext ResultadoEnvio.Rechazado(
                "La dirección del servidor está mal configurada en este kiosko."
            )

        SchIdApi(destino, ajustes.token).registrar(documento, imagenFrente, imagenReverso)
    }
}
