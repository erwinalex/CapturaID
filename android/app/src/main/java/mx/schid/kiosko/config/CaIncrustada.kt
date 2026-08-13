package mx.schid.kiosko.config

import java.io.InputStream
import java.security.MessageDigest
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Lee el certificado de la CA que se compiló dentro del APK.
 *
 * Existe para poder responder en el kiosko, sin cables ni adb, la única
 * pregunta que importa cuando el saludo TLS falla por desconfianza: **¿es esta
 * la misma CA que firmó el certificado del servidor?**
 *
 * La huella se reporta en SHA-1 porque es la que Windows llama "Thumbprint" y
 * la que se ve en `certlm.msc` y en `$cert.Thumbprint`. Comparar dos cadenas de
 * hex es inmediato; adivinar por el nombre del sujeto, no —dos CAs generadas en
 * momentos distintos se llaman igual.
 */
object CaIncrustada {

    /**
     * El sujeto que lleva el marcador de posición del repositorio. Si la app se
     * compiló sin reemplazarlo, esto es lo que hay dentro y no va a conectar
     * con ningún servidor.
     */
    private const val MARCA_DEL_MARCADOR = "REEMPLAZAR ESTE ARCHIVO"

    data class Datos(
        val sujeto: String,
        val emisor: String,
        /** SHA-1 en mayúsculas y sin separadores, como lo muestra Windows. */
        val huellaSha1: String,
        val huellaSha256: String,
        val valeDesde: String,
        val valeHasta: String,
        val esMarcador: Boolean,
        val vencido: Boolean
    )

    fun leer(entrada: InputStream, ahora: Date = Date()): Datos? = try {
        val certificado = CertificateFactory.getInstance("X.509")
            .generateCertificate(entrada) as X509Certificate

        Datos(
            sujeto = certificado.subjectX500Principal.name,
            emisor = certificado.issuerX500Principal.name,
            huellaSha1 = huella(certificado, "SHA-1"),
            huellaSha256 = huella(certificado, "SHA-256"),
            valeDesde = fecha(certificado.notBefore),
            valeHasta = fecha(certificado.notAfter),
            esMarcador = certificado.subjectX500Principal.name.contains(MARCA_DEL_MARCADOR),
            vencido = certificado.notAfter.before(ahora)
        )
    } catch (e: Exception) {
        null
    }

    private fun huella(certificado: X509Certificate, algoritmo: String): String =
        MessageDigest.getInstance(algoritmo)
            .digest(certificado.encoded)
            .joinToString("") { "%02X".format(it) }

    private fun fecha(valor: Date): String =
        SimpleDateFormat("yyyy-MM-dd", Locale.US).format(valor)
}
