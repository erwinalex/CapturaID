package mx.schid.kiosko.datos

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class MrzTest {

    /**
     * Espécimen de la propia norma ICAO 9303 (el pasaporte de ejemplo de
     * "Utopía"). Sirve como vector de verificación: sus dígitos de control son
     * los correctos por definición, así que si nuestra implementación no los
     * valida, el error es nuestro.
     */
    private val linea1 = "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<"
    private val linea2 = "L898902C36UTO7408122F1204159ZE184226B<<<<<10"

    @Test
    fun `el especimen de la norma mide 44 caracteres por linea`() {
        assertEquals(Mrz.LARGO_LINEA, linea1.length)
        assertEquals(Mrz.LARGO_LINEA, linea2.length)
    }

    @Test
    fun `los digitos de control del especimen cuadran`() {
        val pasaporte = Mrz.leer(linea1, linea2)

        assertNotNull(pasaporte)
        assertTrue(
            "Los dígitos de control del espécimen oficial deben validar; " +
                "si esto falla, el algoritmo está mal implementado.",
            pasaporte!!.consistente
        )
    }

    @Test
    fun `extrae los campos del especimen`() {
        val pasaporte = Mrz.leer(linea1, linea2)!!

        assertEquals("L898902C3", pasaporte.numero)
        assertEquals("UTO", pasaporte.paisEmisor)
        assertEquals("UTO", pasaporte.nacionalidad)
        assertEquals("ERIKSSON", pasaporte.apellidos)
        assertEquals("ANNA MARIA", pasaporte.nombres)
        assertEquals("740812", pasaporte.fechaNacimiento)
        assertEquals("F", pasaporte.sexo)
        assertEquals("120415", pasaporte.fechaVencimiento)
    }

    @Test
    fun `arma el nombre completo con nombres antes de apellidos`() {
        assertEquals("ANNA MARIA ERIKSSON", Mrz.leer(linea1, linea2)!!.nombreCompleto)
    }

    /**
     * Lo que hacen útiles los dígitos de control: detectar un OCR mal leído
     * antes de mandar basura al servidor. Con la INE esto no se puede hacer.
     */
    @Test
    fun `detecta un numero de pasaporte mal leido`() {
        val alterada = "L898902C99UTO7408122F1204159ZE184226B<<<<<10"

        assertFalse(Mrz.leer(linea1, alterada)!!.consistente)
    }

    @Test
    fun `detecta una fecha de nacimiento mal leida`() {
        val alterada = "L898902C36UTO7508122F1204159ZE184226B<<<<<10"

        assertFalse(Mrz.leer(linea1, alterada)!!.consistente)
    }

    @Test
    fun `rechaza lineas de largo equivocado`() {
        assertNull(Mrz.leer(linea1.dropLast(1), linea2))
        assertNull(Mrz.leer(linea1, linea2 + "X"))
    }

    @Test
    fun `rechaza un documento que no es pasaporte`() {
        assertNull(Mrz.leer("I<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<", linea2))
    }

    @Test
    fun `encuentra la mrz dentro del texto que devuelve el ocr`() {
        val ocr = """
            PASAPORTE / PASSPORT
            UTOPIA
            Apellidos / Surname
            ERIKSSON
            Nombres / Given names
            ANNA MARIA
            $linea1
            $linea2
        """.trimIndent()

        val pasaporte = Mrz.buscarEn(ocr)

        assertNotNull(pasaporte)
        assertEquals("L898902C3", pasaporte!!.numero)
        assertTrue(pasaporte.consistente)
    }

    /**
     * El OCR confunde seguido el relleno '<' con otros signos y mete espacios.
     * Si no se normalizara, ninguna línea daría los 44 exactos y la MRZ se
     * descartaría entera.
     */
    @Test
    fun `tolera los signos con los que el ocr confunde el relleno`() {
        val sucia1 = linea1.replace("<", "«")
        val sucia2 = linea2.replace("<", "«").replace("C36", "C3 6")

        val pasaporte = Mrz.buscarEn("$sucia1\n$sucia2")

        assertNotNull(pasaporte)
        assertEquals("L898902C3", pasaporte!!.numero)
    }

    @Test
    fun `devuelve null cuando no hay mrz`() {
        assertNull(Mrz.buscarEn("PASAPORTE\nANNA MARIA ERIKSSON\nUTOPIA"))
    }

    // ------------------------------------------------------ dígito de control ---

    @Test
    fun `el digito de control usa los pesos 7 3 1`() {
        // Del propio espécimen: el número L898902C3 tiene dígito 6.
        assertEquals(6, Mrz.digitoDeControl("L898902C3"))
        // La fecha de nacimiento 740812 tiene dígito 2.
        assertEquals(2, Mrz.digitoDeControl("740812"))
        // El vencimiento 120415 tiene dígito 9.
        assertEquals(9, Mrz.digitoDeControl("120415"))
    }

    @Test
    fun `el relleno vale cero`() {
        assertEquals(0, Mrz.digitoDeControl("<<<<<<"))
    }

    @Test
    fun `rechaza caracteres fuera del alfabeto de la mrz`() {
        assertEquals(-1, Mrz.digitoDeControl("ABC-123"))
    }

    // ------------------------------------------------------------------ edad ---

    @Test
    fun `calcula la edad deduciendo el siglo`() {
        // 740812 con año actual 2025: 1974, no 2074.
        assertEquals(50, Mrz.edad("740812", anio = 2025, mes = 6, dia = 1))
    }

    @Test
    fun `una fecha que quedaria en el futuro pertenece al siglo pasado`() {
        // 990101 en 2025 tiene que ser 1999, no 2099.
        assertEquals(26, Mrz.edad("990101", anio = 2025, mes = 6, dia = 1))
    }

    @Test
    fun `un año de dos digitos menor al actual es de este siglo`() {
        // 100101 en 2025 es 2010.
        assertEquals(15, Mrz.edad("100101", anio = 2025, mes = 6, dia = 1))
    }

    @Test
    fun `no cuenta el cumpleanos que todavia no llega`() {
        assertEquals(51, Mrz.edad("740812", anio = 2025, mes = 8, dia = 12))
        assertEquals(50, Mrz.edad("740812", anio = 2025, mes = 8, dia = 11))
    }

    @Test
    fun `rechaza una fecha mal formada`() {
        assertNull(Mrz.edad("74081", anio = 2025, mes = 6, dia = 1))
        assertNull(Mrz.edad("7408AB", anio = 2025, mes = 6, dia = 1))
        assertNull(Mrz.edad("741312", anio = 2025, mes = 6, dia = 1))
    }
}
