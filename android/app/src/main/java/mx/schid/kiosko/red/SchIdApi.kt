package mx.schid.kiosko.red

import mx.schid.kiosko.datos.DocumentoCapturado
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.IOException
import javax.net.ssl.SSLException
import java.util.concurrent.TimeUnit

/** Lo que la API contesta. No trae datos del huésped, solo el desenlace. */
data class RegistroExitoso(
    val id: Long,
    /** "Creado", "Actualizado" o "SinCambios". */
    val resultado: String,
    val camposActualizados: List<String>
)

sealed interface ResultadoEnvio {
    data class Exito(val registro: RegistroExitoso) : ResultadoEnvio

    /** El token no sirve o le falta el rol Captura. No se reintenta: hay que reconfigurar. */
    data object TokenRechazado : ResultadoEnvio

    /** La API rechazó los datos (CURP vacío, imagen que no es JPEG). Tampoco se reintenta. */
    data class Rechazado(val motivo: String) : ResultadoEnvio

    /**
     * El certificado del servidor no es de confianza para esta app. Se separa
     * de FalloTemporal porque el remedio no tiene nada que ver: reintentar no
     * sirve, hay que revisar el certificado.
     */
    data class CertificadoRechazado(val detalle: String) : ResultadoEnvio

    /** Falla de red o del servidor. Reintentar sí tiene sentido. */
    data class FalloTemporal(val motivo: String) : ResultadoEnvio
}

/** Lo que devuelve la prueba de conexión de la pantalla de ajustes. */
data class ResultadoPrueba(val correcto: Boolean, val mensaje: String)

/**
 * Cliente de la API de SchId.
 *
 * No se usa Retrofit a propósito: es una sola llamada y una construcción de
 * multipart, y así hay una dependencia menos que mantener en un equipo que
 * probablemente no se actualice seguido.
 *
 * El certificado del servidor NO se maneja aquí: la confianza en la CA propia se
 * declara en res/xml/network_security_config.xml, que es la vía que Android
 * ofrece para esto y no obliga a escribir un TrustManager a mano (escribirlo mal
 * es la forma más común de acabar aceptando cualquier certificado).
 */
class SchIdApi(
    private val urlBase: String,
    private val token: String,
    private val cliente: OkHttpClient = clientePorOmision()
) {

    companion object {
        private val JPEG = "image/jpeg".toMediaType()

        fun clientePorOmision(): OkHttpClient = OkHttpClient.Builder()
            // Tiempos cortos: si la red local no responde, es mejor avisarle al
            // huésped que se quede mirando una pantalla congelada.
            .connectTimeout(10, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .retryOnConnectionFailure(true)
            .build()
    }

    fun registrar(
        documento: DocumentoCapturado,
        imagenFrente: ByteArray?,
        imagenReverso: ByteArray?
    ): ResultadoEnvio {
        val cuerpo = MultipartBody.Builder()
            .setType(MultipartBody.FORM)
            .apply {
                addFormDataPart("Curp", documento.identidad)
                documento.nombre?.let { addFormDataPart("Nombre", it) }
                documento.direccion?.let { addFormDataPart("Direccion", it) }
                documento.nacionalidad?.let { addFormDataPart("Nacionalidad", it) }
                documento.residencia?.let { addFormDataPart("Residencia", it) }
                documento.edad?.let { addFormDataPart("Edad", it.toString()) }

                imagenFrente?.let {
                    addFormDataPart("imagenFrente", "frente.jpg", it.toRequestBody(JPEG))
                }
                imagenReverso?.let {
                    addFormDataPart("imagenReverso", "reverso.jpg", it.toRequestBody(JPEG))
                }
            }
            .build()

        val peticion = Request.Builder()
            .url("${urlBase.trimEnd('/')}/api/personas/registro")
            .addHeader("X-Api-Key", token)
            .post(cuerpo)
            .build()

        return try {
            cliente.newCall(peticion).execute().use { respuesta ->
                when {
                    respuesta.isSuccessful -> interpretar(respuesta.body?.string())
                    respuesta.code == 401 || respuesta.code == 403 -> ResultadoEnvio.TokenRechazado
                    respuesta.code == 400 -> ResultadoEnvio.Rechazado(
                        respuesta.body?.string()?.take(300).orEmpty()
                    )
                    else -> ResultadoEnvio.FalloTemporal("El servidor respondió ${respuesta.code}.")
                }
            }
        } catch (e: SSLException) {
            // Un handshake rechazado falla al instante y NO deja rastro en el
            // servidor: la conexión se corta antes de que llegue ninguna
            // petición. Sin distinguirlo, el síntoma es indistinguible de un
            // problema de red y no hay por dónde empezar a buscar.
            ResultadoEnvio.CertificadoRechazado(e.message ?: "Certificado rechazado.")
        } catch (e: IOException) {
            ResultadoEnvio.FalloTemporal(e.message ?: "No se pudo conectar con el servidor.")
        }
    }

    /**
     * Prueba la conexión sin registrar nada, para la pantalla de ajustes.
     *
     * Se pide un endpoint que el token del kiosko NO tiene permitido: un 403 es
     * la mejor respuesta posible, porque demuestra que el TLS se estableció,
     * que el servidor recibió la petición y que reconoció el token. Distinguir
     * ese caso del 401 dice además si el token es el correcto.
     */
    fun probarConexion(): ResultadoPrueba {
        val peticion = Request.Builder()
            .url("${urlBase.trimEnd('/')}/api/personas/curp/PRUEBA")
            .addHeader("X-Api-Key", token)
            .get()
            .build()

        return try {
            cliente.newCall(peticion).execute().use { respuesta ->
                when (respuesta.code) {
                    403 -> ResultadoPrueba(true, "Conexión correcta. El servidor acepta este token.")
                    401 -> ResultadoPrueba(false, "El servidor rechazó el token. Revisa que sea el que está en appsettings.json.")
                    200, 404 -> ResultadoPrueba(true, "Conexión correcta (este token además puede consultar).")
                    else -> ResultadoPrueba(false, "El servidor respondió ${respuesta.code}.")
                }
            }
        } catch (e: SSLException) {
            ResultadoPrueba(false, explicarFalloTls(e))
        } catch (e: IOException) {
            ResultadoPrueba(
                false,
                "No se pudo conectar.\n\nRevisa la IP y el puerto, que el servicio esté " +
                    "corriendo y que el firewall de Windows permita el puerto.\n\n" +
                    "Detalle: ${e.message}"
            )
        }
    }

    /**
     * Traduce un fallo de TLS a algo accionable.
     *
     * La distinción importa porque manda a revisar lugares opuestos: si el
     * problema fuera la confianza en la CA, Android lo dice con "trust anchor"
     * y hay que tocar la app; si en cambio el servidor cierra la conexión, la
     * app está bien y lo que falla es el certificado del servidor —lo más común,
     * que el proceso no pueda leer su llave privada.
     */
    private fun explicarFalloTls(e: SSLException): String {
        val detalle = e.message.orEmpty()
        val texto = detalle.lowercase()

        val causa = when {
            "trust anchor" in texto || "certpath" in texto || "certificate_unknown" in texto ->
                "La app no confía en la CA que firmó el certificado del servidor.\n\n" +
                    "Copia el schid_ca.crt que generó el script sobre " +
                    "app/src/main/res/raw/schid_ca.crt y vuelve a compilar la app."

            "hostname" in texto || "subject alternative" in texto || "no subject alt" in texto ->
                "El certificado no incluye la dirección con la que estás conectando.\n\n" +
                    "Vuelve a generarlo pasando esa IP en -Direcciones."

            "closed" in texto || "reset" in texto || "eof" in texto ->
                "El servidor cerró la conexión durante el saludo TLS. La app está bien; " +
                    "el problema está del lado del servidor.\n\n" +
                    "Lo más común es que el proceso no pueda leer la llave privada del " +
                    "certificado. Revisa el log del servicio al arrancar: ahí dice qué " +
                    "certificado cargó y si tiene llave privada accesible."

            else ->
                "Falló el saludo TLS con el servidor."
        }

        return "$causa\n\nDetalle: $detalle"
    }

    private fun interpretar(json: String?): ResultadoEnvio {
        if (json.isNullOrBlank()) {
            return ResultadoEnvio.FalloTemporal("El servidor respondió sin contenido.")
        }

        return try {
            val objeto = JSONObject(json)
            val campos = objeto.optJSONArray("camposActualizados")
            ResultadoEnvio.Exito(
                RegistroExitoso(
                    id = objeto.getLong("id"),
                    resultado = objeto.optString("resultado"),
                    camposActualizados = buildList {
                        for (i in 0 until (campos?.length() ?: 0)) {
                            add(campos!!.getString(i))
                        }
                    }
                )
            )
        } catch (e: Exception) {
            ResultadoEnvio.FalloTemporal("No se entendió la respuesta del servidor.")
        }
    }
}
