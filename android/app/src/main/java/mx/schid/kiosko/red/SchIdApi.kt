package mx.schid.kiosko.red

import mx.schid.kiosko.datos.IneCapturada
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.IOException
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

    /** Falla de red o del servidor. Reintentar sí tiene sentido. */
    data class FalloTemporal(val motivo: String) : ResultadoEnvio
}

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
        ine: IneCapturada,
        imagenFrente: ByteArray?,
        imagenReverso: ByteArray?
    ): ResultadoEnvio {
        val cuerpo = MultipartBody.Builder()
            .setType(MultipartBody.FORM)
            .apply {
                addFormDataPart("Curp", ine.curp)
                ine.nombre?.let { addFormDataPart("Nombre", it) }
                ine.direccion?.let { addFormDataPart("Direccion", it) }
                ine.nacionalidad?.let { addFormDataPart("Nacionalidad", it) }
                ine.residencia?.let { addFormDataPart("Residencia", it) }
                ine.edad?.let { addFormDataPart("Edad", it.toString()) }

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
        } catch (e: IOException) {
            ResultadoEnvio.FalloTemporal(e.message ?: "No se pudo conectar con el servidor.")
        }
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
