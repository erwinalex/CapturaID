package mx.schid.kiosko.datos

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class CurpTest {

    // CURP inventado que cumple la estructura oficial. No corresponde a
    // ninguna persona: las letras y consonantes son consistentes entre sí pero
    // la clave no está registrada.
    private val curpValido = "MELM850315HDFNPR07"

    @Test
    fun `acepta un curp con la estructura correcta`() {
        assertTrue(Curp.tieneEstructuraValida(curpValido))
    }

    @Test
    fun `rechaza un curp de largo equivocado`() {
        assertFalse(Curp.tieneEstructuraValida("MELM850315HDFNPR0"))
        assertFalse(Curp.tieneEstructuraValida("MELM850315HDFNPR077"))
    }

    @Test
    fun `rechaza un mes o dia imposibles`() {
        assertFalse(Curp.tieneEstructuraValida("MELM851315HDFNPR07"))
        assertFalse(Curp.tieneEstructuraValida("MELM850332HDFNPR07"))
    }

    @Test
    fun `rechaza una entidad que no existe`() {
        // "ZZ" no es una entidad federativa válida.
        assertFalse(Curp.tieneEstructuraValida("MELM850315HZZNPR07"))
    }

    @Test
    fun `rechaza un sexo distinto de H o M`() {
        assertFalse(Curp.tieneEstructuraValida("MELM850315XDFNPR07"))
    }

    @Test
    fun `la segunda posicion tiene que ser vocal o X`() {
        assertFalse(Curp.tieneEstructuraValida("MZLM850315HDFNPR07"))
    }

    /**
     * Lo que hace útil al lector: no depende de en qué posición venga el CURP
     * dentro del código de barras, porque lo busca por forma.
     */
    @Test
    fun `encuentra el curp dentro de un texto con otros campos`() {
        val contenido = "0123456789|JUAN PEREZ LOPEZ|$curpValido|CALLE FALSA 123|2019"
        assertEquals(curpValido, Curp.buscarEn(contenido))
    }

    @Test
    fun `no confunde una cadena de 18 caracteres que no es curp`() {
        assertNull(Curp.buscarEn("123456789012345678|OTRO CAMPO"))
    }

    @Test
    fun `devuelve null cuando no hay ningun curp`() {
        assertNull(Curp.buscarEn("SIN NADA UTIL AQUI"))
    }

    @Test
    fun `normaliza a mayusculas y sin espacios`() {
        assertEquals(curpValido, Curp.normalizar("  melm850315hdfnpr07  "))
    }

    @Test
    fun `saca la fecha de nacimiento del propio curp`() {
        assertEquals("850315", Curp.fechaNacimiento(curpValido))
    }

    /**
     * El siglo no viene explícito en el CURP: se deduce de la homoclave, que es
     * un dígito para quienes nacieron antes del 2000 y una letra para después.
     */
    @Test
    fun `una homoclave con digito significa siglo veinte`() {
        assertEquals(40, Curp.edad(curpValido, anioActual = 2025, mesActual = 6, diaActual = 1))
    }

    @Test
    fun `una homoclave con letra significa siglo veintiuno`() {
        val nacidoEn2005 = "MELM050315HDFNPRA5"
        assertEquals(20, Curp.edad(nacidoEn2005, anioActual = 2025, mesActual = 6, diaActual = 1))
    }

    @Test
    fun `no cuenta el cumpleanos que todavia no llega`() {
        assertEquals(39, Curp.edad(curpValido, anioActual = 2025, mesActual = 2, diaActual = 1))
        assertEquals(39, Curp.edad(curpValido, anioActual = 2025, mesActual = 3, diaActual = 14))
        assertEquals(40, Curp.edad(curpValido, anioActual = 2025, mesActual = 3, diaActual = 15))
    }

    @Test
    fun `no calcula edad de un curp mal formado`() {
        assertNull(Curp.edad("NO ES UN CURP", 2025, 6, 1))
    }

    @Test
    fun `el digito verificador no acepta caracteres fuera del alfabeto`() {
        assertFalse(Curp.digitoVerificadorCoincide("MELM850315HDFNPR@7"))
    }

    @Test
    fun `el digito verificador rechaza un largo equivocado`() {
        assertFalse(Curp.digitoVerificadorCoincide("MELM850315"))
    }

    /**
     * El OCR parte seguido el CURP con espacios o saltos de línea. Si solo se
     * buscara sobre el texto tal cual, ninguna ventana de 18 caracteres lo
     * contendría completo y una INE real nunca se reconocería.
     */
    @Test
    fun `encuentra un curp que el ocr partio con espacios`() {
        assertEquals(curpValido, Curp.buscarEn("CURP MELM850315 HDFNPR07"))
        assertEquals(curpValido, Curp.buscarEn("CURP\nMELM850315\nHDFNPR07"))
        assertEquals(curpValido, Curp.buscarEn("MELM 8503 15HD FNPR07"))
    }

    @Test
    fun `sigue funcionando con el curp de corrido`() {
        assertEquals(curpValido, Curp.buscarEn("CURP $curpValido FECHA"))
    }

    /**
     * Compactar el texto no debe inventar CURPs pegando el final de un campo
     * con el principio del siguiente.
     */
    @Test
    fun `compactar no inventa curps donde no los hay`() {
        assertNull(Curp.buscarEn("12345 67890 12345 67890"))
        assertNull(Curp.buscarEn("NOMBRE JUAN\nAPELLIDO PEREZ\nSECCION 1234"))
    }
}
