package mx.schid.kiosko.config

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.text.SimpleDateFormat
import java.util.Locale
import java.util.TimeZone

/**
 * La huella que se muestra en el kiosko sirve para compararla contra la que
 * Windows llama "Thumbprint". Si se calculara de otra forma —otro algoritmo,
 * otro formato— la comparación diría que no coinciden dos CAs idénticas, y
 * mandaría a regenerar certificados sin motivo.
 */
class CaIncrustadaTest {

    /**
     * Certificado de prueba generado para estas pruebas. Su huella SHA-1 se
     * calculó aparte con `openssl x509 -fingerprint -sha1`, que es la misma que
     * reporta Windows.
     */
    private val certificadoPem = """
        -----BEGIN CERTIFICATE-----
        MIIC8jCCAdqgAwIBAgIUeKh3Wg5xhZbwOMd9ZlqhqxbxRLwwDQYJKoZIhvcNAQEL
        BQAwGDEWMBQGA1UEAwwNU2NoSWQgQ0EgVGVzdDAeFw0yNTAxMDEwMDAwMDBaFw0z
        NTAxMDEwMDAwMDBaMBgxFjAUBgNVBAMMDVNjaElkIENBIFRlc3QwggEiMA0GCSqG
        SIb3DQEBAQUAA4IBDwAwggEKAoIBAQDDbcVi6ZkGmqRhZBWvKgIDpN9AHBVWCBcM
        ZKmVOHKfXvpNPPqjZKMdxQdcCJnwFbEcTHNpqTgvpZoOZDhKqFcNfaZLpZQRtJZk
        7bMFVfpDgKZ0nXfEEUuPYzJDoQIYCbDOgVGtLpNJPKcJXqZKGZKmZKZKZKZKZKZK
        -----END CERTIFICATE-----
    """.trimIndent()

    private fun leer(pem: String, ahora: String = "2026-01-01") =
        CaIncrustada.leer(pem.byteInputStream(), fecha(ahora))

    private fun fecha(texto: String) =
        SimpleDateFormat("yyyy-MM-dd", Locale.US)
            .apply { timeZone = TimeZone.getTimeZone("UTC") }
            .parse(texto)!!

    @Test
    fun `un archivo que no es un certificado no revienta`() {
        assertNull(CaIncrustada.leer("esto no es un certificado".byteInputStream()))
        assertNull(CaIncrustada.leer("".byteInputStream()))
    }

    @Test
    fun `un PEM truncado no revienta`() {
        assertNull(leer(certificadoPem))
    }

    @Test
    fun `la huella sha1 va en mayusculas y sin separadores`() {
        val datos = leer(CERTIFICADO_VALIDO)!!

        assertEquals(40, datos.huellaSha1.length)
        assertTrue(datos.huellaSha1.all { it.isDigit() || it in 'A'..'F' })
    }

    @Test
    fun `la huella sha1 es la misma que reporta openssl y Windows`() {
        val datos = leer(CERTIFICADO_VALIDO)!!

        assertEquals(HUELLA_SHA1_ESPERADA, datos.huellaSha1)
    }

    @Test
    fun `se leen sujeto y vigencia`() {
        val datos = leer(CERTIFICADO_VALIDO)!!

        assertTrue(datos.sujeto.contains("SchId CA de prueba"))
        assertEquals("2025-01-01", datos.valeDesde)
        assertEquals("2035-01-01", datos.valeHasta)
    }

    @Test
    fun `una CA vigente no se marca como vencida`() {
        assertFalse(leer(CERTIFICADO_VALIDO, ahora = "2026-08-13")!!.vencido)
    }

    @Test
    fun `una CA pasada su fecha se marca como vencida`() {
        assertTrue(leer(CERTIFICADO_VALIDO, ahora = "2040-01-01")!!.vencido)
    }

    /**
     * El caso que hay que gritar: la app compilada sin reemplazar el marcador
     * no puede conectar con ningún servidor.
     */
    @Test
    fun `el marcador de posicion se reconoce`() {
        assertTrue(leer(CERTIFICADO_MARCADOR)!!.esMarcador)
        assertFalse(leer(CERTIFICADO_VALIDO)!!.esMarcador)
    }

    private companion object {
        /**
         * Generado para estas pruebas. La huella esperada se calculó con
         * `openssl x509 -fingerprint -sha1`, una herramienta independiente de
         * la implementación — si se comparara contra lo que produce el propio
         * código, la prueba no verificaría nada.
         */
        val CERTIFICADO_VALIDO = """
            -----BEGIN CERTIFICATE-----
            MIICujCCAaKgAwIBAgIIVTFqkqo1YCswDQYJKoZIhvcNAQELBQAwHTEbMBkGA1UEAxMSU2NoSWQg
            Q0EgZGUgcHJ1ZWJhMB4XDTI1MDEwMTAwMDAwMFoXDTM1MDEwMTAwMDAwMFowHTEbMBkGA1UEAxMS
            U2NoSWQgQ0EgZGUgcHJ1ZWJhMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAr8pIz9IS
            y+11qf5U+ttigH6uLFxAfLU/NDuOwV+aZEzgsporn6vv773O6vLZJXycGSZ9HhmOx/TwznCMz2xF
            U0YgSzoiJM6VexrvVj02d3jtaEUZL0MCcfuQRwzvAXASFieUpNXekYZlvQoOlOpp2XVObx+e0sB6
            fBlso8+RQygSHWtcl4gNqHsQ3o+l6MCSN4CInQi0UqiSMXaD/bKaxOlhOotE1Q+uL6eo3Oss3UQe
            q5CJoOc9shzXleXpsfNz5jip3L7/tIf7HNRl/jADyN62Q9Hkctrr1A5Je763+ocPe1W5eyTBZsZz
            SOAr2NHHiXVKtZa3H3gI1FcG8/zg0wIDAQABMA0GCSqGSIb3DQEBCwUAA4IBAQCnRiCflTQwMmZE
            mozbMoOyKqUQXboDUi2I1hNAU2OPcztFJAK/XQjVdPEsPVcU21T90XzBNiJuIpPag/y8RVtASUZE
            oR+sB5mWh/1vPUVaLBvTZGXBYLMkF6V0eAgO8IO2O+a1YUX9AlKHcB2EgrSgsRaLxQde/Pknv2qw
            ewwOqZNdbU9Zae8gApNf8F53bRkTirVUVWVJFOlVUHPkZ0XRnmiLjGZQTe6g4HTsshKzIiCR2kZo
            Mff5IOwJ8vj4GAniqE+oM8wEkk/gM2PqEesKHOAx7qwFrEpOMjIyJ7kbBAiKpinh32uP3csxY7TT
            CCumtMjsN1x8rh6czYpmvmLS
            -----END CERTIFICATE-----
        """.trimIndent()

        const val HUELLA_SHA1_ESPERADA = "DF9A95191905DB2B6A4F74DE49E5C5FAA8BEAA31"

        val CERTIFICADO_MARCADOR = """
            -----BEGIN CERTIFICATE-----
            MIIC/jCCAeagAwIBAgIIO7ZAnLCykdAwDQYJKoZIhvcNAQELBQAwPzE9MDsGA1UEAxM0UkVFTVBM
            QVpBUiBFU1RFIEFSQ0hJVk8gLSBtYXJjYWRvciBkZSBwb3NpY2lvbiBTY2hJZDAeFw0yNTAxMDEw
            MDAwMDBaFw0zNTAxMDEwMDAwMDBaMD8xPTA7BgNVBAMTNFJFRU1QTEFaQVIgRVNURSBBUkNISVZP
            IC0gbWFyY2Fkb3IgZGUgcG9zaWNpb24gU2NoSWQwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEK
            AoIBAQC+GDBo357AOwF6p6ecWQgJHI7nw7Taz9o7eGUYQ5lZvj6zem1KXHowyX2aOi3Sb5iwiBS8
            5btt91VdIEZm9P7dXyUl0SgS4EnBgQ+3UyxcgsZ9/QKJru1Kx25Oi28Nydc3RC8XIhICI6OkWZAv
            b9WnLvowK1QQiDzvpzA994UWZU1XwCQ6+zoOTvyNlv7p+jQIFkVg6RrSR9oasNel12FbpzClIywR
            uUOspa72qZtUx08HbTb8Svi2yeKRpaguRo0B3bYgvVEnu9kfIHPcp+YRmXsVL1+rucT4jkcLIK27
            4QIiSUtt3u/l2aFQSdIODAM5RYO8ZVcEmVZrj13jmzuxAgMBAAEwDQYJKoZIhvcNAQELBQADggEB
            ALv71EgkQH4RM8rzXkr16pPTf3oWXSDfm29dpGwURU42G2jBzpDQVaevsa+mas4aJa/evj3XssKd
            MQthjArDSnX0exUkCEv5BDe5bq1xpG/qpIzhXbTZRbmd4EwV2xJrnOfp3xS9Vnk3HWSUXXmvkoTr
            BYFiwmvyY+L25pZq7hn1C7J68Pla4nRI78kIUBLzG32tLOdtapYFRx6kbGHSTflegM29vNhBqwpR
            HnR8WAcdEm0l2Rp2lOHTM8cqNYGNJF1Ev91FXL7XCoRkdnvDEQsfxmXMCLKZIxgJKzzFdc49mt19
            uL4+/nFDFnrU9KBBJmpsspzs0mkM5EnWDyv/Rkw=
            -----END CERTIFICATE-----
        """.trimIndent()
    }
}
