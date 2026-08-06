package mx.schid.kiosko.datos

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LectorIneTest {

    private val lector = LectorIne()
    private val curp = "MELM850315HDFNPR07"

    private fun leer(contenido: String) =
        lector.leer(contenido, anio = 2025, mes = 6, dia = 1)

    @Test
    fun `saca el curp de un contenido separado por pipes`() {
        val resultado = leer("0123|JUAN PEREZ LOPEZ|$curp|CALLE FALSA 123 COL CENTRO|2019")

        assertTrue(resultado is ResultadoLectura.Exito)
        assertEquals(curp, (resultado as ResultadoLectura.Exito).ine.curp)
    }

    @Test
    fun `funciona con otros separadores`() {
        listOf('^', '\n', '\t', '~').forEach { separador ->
            val contenido = listOf("0123", "JUAN PEREZ LOPEZ", curp, "CALLE FALSA 123")
                .joinToString(separador.toString())

            val resultado = leer(contenido)
            assertTrue("Falló con el separador '$separador'", resultado is ResultadoLectura.Exito)
        }
    }

    /**
     * El caso que hace falta manejar bien: un formato que no reconocemos. Aun
     * así el CURP sale, porque se busca por forma y no por posición.
     */
    @Test
    fun `saca el curp aunque el formato sea desconocido`() {
        val resultado = leer("XXYYZZ$curp!!!!basura+++")

        assertTrue(resultado is ResultadoLectura.Exito)
        assertEquals(curp, (resultado as ResultadoLectura.Exito).ine.curp)
    }

    @Test
    fun `reporta que no hubo curp sin exponer el contenido`() {
        val resultado = leer("CODIGO|QUE|NO|TRAE|CURP")

        assertTrue(resultado is ResultadoLectura.SinCurp)
        assertEquals(23, (resultado as ResultadoLectura.SinCurp).largoContenido)
    }

    @Test
    fun `toma como nombre el campo de puras letras mas largo`() {
        val resultado = leer("0123|JUAN PEREZ LOPEZ|$curp|CALLE FALSA 123 COL CENTRO")

        assertEquals("JUAN PEREZ LOPEZ", (resultado as ResultadoLectura.Exito).ine.nombre)
    }

    @Test
    fun `toma como direccion el campo que mezcla letras y numeros`() {
        val resultado = leer("0123|JUAN PEREZ LOPEZ|$curp|CALLE FALSA 123 COL CENTRO")

        assertEquals(
            "CALLE FALSA 123 COL CENTRO",
            (resultado as ResultadoLectura.Exito).ine.direccion
        )
    }

    /**
     * Si no se reconoce el formato, es preferible mandar los campos vacíos que
     * mandar basura: la API interpreta un campo vacío como "no se pudo leer" y
     * conserva el valor anterior, mientras que un valor equivocado lo pisaría.
     */
    @Test
    fun `sin campos reconocibles manda nombre y direccion vacios`() {
        val resultado = leer(curp)
        val ine = (resultado as ResultadoLectura.Exito).ine

        assertNull(ine.nombre)
        assertNull(ine.direccion)
    }

    @Test
    fun `calcula la edad a partir del curp`() {
        val resultado = leer("0123|$curp")

        assertEquals(40, (resultado as ResultadoLectura.Exito).ine.edad)
    }

    @Test
    fun `nunca toma el curp como nombre`() {
        val resultado = leer("$curp|OTRO")

        assertEquals(null, (resultado as ResultadoLectura.Exito).ine.nombre)
    }
}
