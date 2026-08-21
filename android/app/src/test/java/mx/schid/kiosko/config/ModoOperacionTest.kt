package mx.schid.kiosko.config

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ModoOperacionTest {

    @Test
    fun `en modo kiosko la app se apodera del dispositivo`() {
        val modo = ModoOperacion.para(modoKiosko = true)

        assertTrue(modo.anclarPantalla)
        assertTrue(modo.mantenerPantallaEncendida)
        assertTrue(modo.puedeSerLanzador)
    }

    @Test
    fun `fuera del modo kiosko el dispositivo queda libre`() {
        val modo = ModoOperacion.para(modoKiosko = false)

        assertFalse(modo.anclarPantalla)
        assertFalse(modo.mantenerPantallaEncendida)
        assertFalse(modo.puedeSerLanzador)
    }

    /**
     * El invariante que no debe perderse nunca.
     *
     * Bloquear capturas protege los datos de la INE que están en pantalla, y eso
     * no tiene que ver con si el dispositivo está anclado. Fuera del modo kiosko
     * hace todavía más falta, porque ahí el teléfono se comparte con otras apps
     * y con otras personas.
     *
     * Esta prueba existe para que quede escrito: si alguien "simplifica" el modo
     * kiosko a una sola bandera y se lleva FLAG_SECURE por delante, falla aquí y
     * no en producción con una INE en la lista de recientes.
     */
    @Test
    fun `las capturas de pantalla se bloquean en los dos modos`() {
        assertTrue(ModoOperacion.para(modoKiosko = true).bloquearCapturas)
        assertTrue(ModoOperacion.para(modoKiosko = false).bloquearCapturas)
    }
}
