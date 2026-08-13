package mx.schid.kiosko.datos

import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.abs

class RecorteCredencialTest {

    /** La foto que produce la cámara del kiosko: 4:3 en vertical. */
    private val anchoFoto = 1080
    private val altoFoto = 1440

    @Test
    fun `el recorte conserva la proporcion de una credencial`() {
        val recorte = RecorteCredencial.calcular(anchoFoto, altoFoto)

        val proporcion = recorte.ancho.toFloat() / recorte.alto
        assertTrue(
            "Proporción $proporcion, se esperaba ~${RecorteCredencial.PROPORCION}",
            abs(proporcion - RecorteCredencial.PROPORCION) < 0.02f
        )
    }

    /**
     * Centrado con tolerancia de un píxel: cuando lo que sobra es impar no hay
     * forma de repartirlo en dos mitades enteras iguales. Un píxel en una foto
     * de 1440 no se ve, y forzar dimensiones pares por esto sería complicar el
     * cálculo para nada.
     */
    @Test
    fun `el recorte queda centrado`() {
        val recorte = RecorteCredencial.calcular(anchoFoto, altoFoto)

        val sobranteDerecha = anchoFoto - (recorte.x + recorte.ancho)
        val sobranteAbajo = altoFoto - (recorte.y + recorte.alto)

        assertTrue("Descentrado a lo ancho", abs(recorte.x - sobranteDerecha) <= 1)
        assertTrue("Descentrado a lo alto", abs(recorte.y - sobranteAbajo) <= 1)
    }

    @Test
    fun `el recorte nunca se sale de la imagen`() {
        val tamanos = listOf(
            1080 to 1440, 1440 to 1080, 3000 to 4000, 480 to 640, 100 to 100, 4000 to 500
        )

        tamanos.forEach { (ancho, alto) ->
            val r = RecorteCredencial.calcular(ancho, alto)

            assertTrue("x negativo en $ancho x $alto", r.x >= 0)
            assertTrue("y negativo en $ancho x $alto", r.y >= 0)
            assertTrue("se pasa a lo ancho en $ancho x $alto", r.x + r.ancho <= ancho)
            assertTrue("se pasa a lo alto en $ancho x $alto", r.y + r.alto <= alto)
            assertTrue("ancho vacío en $ancho x $alto", r.ancho > 0)
            assertTrue("alto vacío en $ancho x $alto", r.alto > 0)
        }
    }

    /**
     * En una imagen apaisada la guía no cabría de alto si se calculara solo por
     * el ancho; ahí tiene que mandar el alto.
     */
    @Test
    fun `en una imagen apaisada el alto manda`() {
        val recorte = RecorteCredencial.calcular(4000, 500)

        assertTrue("Se salió de alto", recorte.alto <= 500)
        val proporcion = recorte.ancho.toFloat() / recorte.alto
        assertTrue(abs(proporcion - RecorteCredencial.PROPORCION) < 0.02f)
    }

    /**
     * El margen existe para que el huésped no tenga que acomodar el documento
     * con precisión: es preferible guardar algo de fondo a cortar la credencial.
     */
    @Test
    fun `deja margen a los lados`() {
        val recorte = RecorteCredencial.calcular(anchoFoto, altoFoto)

        assertTrue("Sin margen lateral", recorte.x > 0)
        assertTrue("Margen excesivo", recorte.ancho > anchoFoto * 0.85)
    }

    /**
     * El recorte tiene que quitar una parte apreciable de la foto: si no, no
     * está resolviendo el problema del fondo que motivó todo esto.
     */
    @Test
    fun `descarta buena parte del fondo`() {
        val recorte = RecorteCredencial.calcular(anchoFoto, altoFoto)

        val proporcionConservada =
            (recorte.ancho.toLong() * recorte.alto).toFloat() / (anchoFoto.toLong() * altoFoto)

        assertTrue(
            "Se conserva el ${(proporcionConservada * 100).toInt()}% de la foto",
            proporcionConservada < 0.5f
        )
    }
}
