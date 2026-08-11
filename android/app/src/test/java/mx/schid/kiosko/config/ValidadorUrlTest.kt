package mx.schid.kiosko.config

import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ValidadorUrlTest {

    @Test
    fun `https siempre se acepta`() {
        assertNull(ValidadorUrl.validar("https://192.168.1.50:7443", permiteHttp = false))
        assertNull(ValidadorUrl.validar("https://192.168.1.50:7443", permiteHttp = true))
    }

    /**
     * Se permite http únicamente en depuración, para poder probar contra un
     * servidor que todavía no tiene certificado.
     */
    @Test
    fun `http se acepta solo cuando esta permitido`() {
        assertNull(ValidadorUrl.validar("http://192.168.1.50:5080", permiteHttp = true))
        assertNotNull(ValidadorUrl.validar("http://192.168.1.50:5080", permiteHttp = false))
    }

    @Test
    fun `el rechazo de http explica por que`() {
        val motivo = ValidadorUrl.validar("http://192.168.1.50:5080", permiteHttp = false)

        assertTrue(motivo!!.contains("claro"))
    }

    @Test
    fun `una direccion vacia se rechaza`() {
        assertNotNull(ValidadorUrl.validar("", permiteHttp = true))
        assertNotNull(ValidadorUrl.validar("   ", permiteHttp = true))
    }

    @Test
    fun `una direccion sin protocolo se rechaza`() {
        assertNotNull(ValidadorUrl.validar("192.168.1.50:7443", permiteHttp = true))
        assertNotNull(ValidadorUrl.validar("ftp://192.168.1.50", permiteHttp = true))
    }

    @Test
    fun `los espacios alrededor no estorban`() {
        assertNull(ValidadorUrl.validar("  https://192.168.1.50:7443  ", permiteHttp = false))
    }

    @Test
    fun `reconoce cuando la direccion va sin cifrar`() {
        assertTrue(ValidadorUrl.esSinCifrar("http://192.168.1.50:5080"))
        assertFalse(ValidadorUrl.esSinCifrar("https://192.168.1.50:7443"))
        assertFalse(ValidadorUrl.esSinCifrar(""))
    }
}
