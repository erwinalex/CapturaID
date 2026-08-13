package mx.schid.kiosko.config

import mx.schid.kiosko.config.DireccionServidor.NOMBRE_CERTIFICADO
import mx.schid.kiosko.config.DireccionServidor.PUERTO_POR_OMISION
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class DireccionServidorTest {

    @Test
    fun `una IP sola apunta a https en el puerto de siempre`() {
        val destino = DireccionServidor.interpretar("192.168.1.226")!!

        assertEquals("192.168.1.226", destino.host)
        assertEquals(PUERTO_POR_OMISION, destino.puerto)
        assertTrue(destino.conTls)
    }

    @Test
    fun `se respeta el puerto que se escriba`() {
        val destino = DireccionServidor.interpretar("192.168.1.226:9443")!!

        assertEquals("192.168.1.226", destino.host)
        assertEquals(9443, destino.puerto)
    }

    @Test
    fun `tambien se acepta un nombre de maquina`() {
        val destino = DireccionServidor.interpretar("servidor-hotel:7443")!!

        assertEquals("servidor-hotel", destino.host)
        assertEquals(7443, destino.puerto)
    }

    /**
     * El corazón del asunto: con TLS se pide siempre el nombre del certificado,
     * nunca la IP. Por eso un solo certificado sirve para las 70 ubicaciones y
     * un solo APK sirve para todas — la IP entra al resolver el nombre, no en
     * la URL.
     */
    @Test
    fun `con TLS la URL lleva el nombre del certificado y no la IP`() {
        val destino = DireccionServidor.interpretar("192.168.1.226")!!

        assertEquals("https://$NOMBRE_CERTIFICADO:$PUERTO_POR_OMISION", destino.url)
    }

    @Test
    fun `dos ubicaciones con IP distinta piden la misma URL`() {
        val una = DireccionServidor.interpretar("192.168.1.226:7443")!!
        val otra = DireccionServidor.interpretar("10.0.0.9:7443")!!

        assertEquals(una.url, otra.url)
    }

    /**
     * Sin TLS no hay certificado que validar, así que se va derecho a la IP.
     */
    @Test
    fun `sin cifrar la URL va directa al host`() {
        val destino = DireccionServidor.interpretar("http://192.168.1.226:5080")!!

        assertFalse(destino.conTls)
        assertEquals("http://192.168.1.226:5080", destino.url)
    }

    @Test
    fun `http sin puerto se queda en el 80`() {
        val destino = DireccionServidor.interpretar("http://192.168.1.226")!!

        assertEquals(80, destino.puerto)
    }

    @Test
    fun `el prefijo https es opcional y no cambia nada`() {
        assertEquals(
            DireccionServidor.interpretar("192.168.1.226:7443"),
            DireccionServidor.interpretar("https://192.168.1.226:7443")
        )
    }

    @Test
    fun `los espacios y la diagonal final no estorban`() {
        val destino = DireccionServidor.interpretar("  192.168.1.226:7443/  ")!!

        assertEquals("192.168.1.226", destino.host)
        assertEquals(7443, destino.puerto)
    }

    @Test
    fun `lo que no se entiende se rechaza`() {
        assertNull(DireccionServidor.interpretar(""))
        assertNull(DireccionServidor.interpretar("   "))
        assertNull(DireccionServidor.interpretar("ftp://192.168.1.226"))
        assertNull(DireccionServidor.interpretar("192.168.1.226:7443:80"))
        assertNull(DireccionServidor.interpretar("192.168.1.226:puerto"))
        assertNull(DireccionServidor.interpretar("192.168.1.226:0"))
        assertNull(DireccionServidor.interpretar("192.168.1.226:70000"))
        assertNull(DireccionServidor.interpretar("servidor del hotel:7443"))
        assertNull(DireccionServidor.interpretar(":7443"))
    }

    @Test
    fun `una direccion con TLS siempre se acepta`() {
        assertNull(DireccionServidor.validar("192.168.1.226:7443", permiteHttp = false))
        assertNull(DireccionServidor.validar("192.168.1.226:7443", permiteHttp = true))
    }

    /**
     * Se permite http únicamente en depuración, para poder probar contra un
     * servidor que todavía no tiene certificado.
     */
    @Test
    fun `http se acepta solo cuando esta permitido`() {
        assertNull(DireccionServidor.validar("http://192.168.1.226:5080", permiteHttp = true))
        assertNotNull(DireccionServidor.validar("http://192.168.1.226:5080", permiteHttp = false))
    }

    @Test
    fun `el rechazo de http explica por que`() {
        val motivo = DireccionServidor.validar("http://192.168.1.226:5080", permiteHttp = false)

        assertTrue(motivo!!.contains("claro"))
    }

    @Test
    fun `una direccion vacia se rechaza`() {
        assertNotNull(DireccionServidor.validar("", permiteHttp = true))
        assertNotNull(DireccionServidor.validar("   ", permiteHttp = true))
    }

    /**
     * El mensaje de una dirección ilegible tiene que traer un ejemplo: quien
     * configura el kiosko no sabe qué forma se espera.
     */
    @Test
    fun `la direccion ilegible se explica con un ejemplo`() {
        val motivo = DireccionServidor.validar("192.168.1.226:puerto", permiteHttp = true)

        assertTrue(motivo!!.contains("192.168.1.226:7443"))
    }

    @Test
    fun `reconoce cuando la direccion va sin cifrar`() {
        assertTrue(DireccionServidor.esSinCifrar("http://192.168.1.226:5080"))
        assertFalse(DireccionServidor.esSinCifrar("192.168.1.226:7443"))
        assertFalse(DireccionServidor.esSinCifrar("https://192.168.1.226:7443"))
        assertFalse(DireccionServidor.esSinCifrar(""))
    }
}
