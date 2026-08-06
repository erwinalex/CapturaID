package mx.schid.kiosko.datos

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LectorDocumentosTest {

    private val lector = LectorDocumentos()
    private val curp = "MELM850315HDFNPR07"

    private fun codigo(contenido: String, origen: OrigenDatos = OrigenDatos.PDF417) =
        lector.leerCodigoIne(contenido, origen, anio = 2025, mes = 6, dia = 1)

    private fun exito(resultado: ResultadoLectura) =
        (resultado as ResultadoLectura.Exito).documento

    // ------------------------------------------------- código de barras INE ---

    @Test
    fun `saca el curp de un contenido separado por pipes`() {
        val documento = exito(codigo("0123|JUAN PEREZ LOPEZ|$curp|CALLE FALSA 123 COL CENTRO|2019"))

        assertEquals(curp, documento.identidad)
        assertEquals(TipoDocumento.INE, documento.tipoDocumento)
    }

    @Test
    fun `funciona con otros separadores`() {
        listOf('^', '\n', '\t', '~').forEach { separador ->
            val contenido = listOf("0123", "JUAN PEREZ LOPEZ", curp, "CALLE FALSA 123")
                .joinToString(separador.toString())

            assertTrue(
                "Falló con el separador '$separador'",
                codigo(contenido) is ResultadoLectura.Exito
            )
        }
    }

    /**
     * El caso que este lector existe para cubrir: un formato que no
     * reconocemos. Aun así el CURP sale, porque se busca por forma y no por
     * posición.
     */
    @Test
    fun `saca el curp aunque el formato sea desconocido`() {
        assertEquals(curp, exito(codigo("XXYYZZ$curp!!!!basura+++")).identidad)
    }

    @Test
    fun `registra si vino de qr o de pdf417`() {
        assertEquals(OrigenDatos.QR, exito(codigo("0123|$curp", OrigenDatos.QR)).origen)
        assertEquals(OrigenDatos.PDF417, exito(codigo("0123|$curp", OrigenDatos.PDF417)).origen)
    }

    @Test
    fun `reporta que no hubo curp sin exponer el contenido`() {
        val resultado = codigo("CODIGO|QUE|NO|TRAE|CURP")

        assertTrue(resultado is ResultadoLectura.NoReconocido)
        assertEquals(23, (resultado as ResultadoLectura.NoReconocido).largoContenido)
    }

    @Test
    fun `toma como nombre el campo de puras letras mas largo`() {
        val documento = exito(codigo("0123|JUAN PEREZ LOPEZ|$curp|CALLE FALSA 123 COL CENTRO"))

        assertEquals("JUAN PEREZ LOPEZ", documento.nombre)
    }

    @Test
    fun `toma como direccion el campo que mezcla letras y numeros`() {
        val documento = exito(codigo("0123|JUAN PEREZ LOPEZ|$curp|CALLE FALSA 123 COL CENTRO"))

        assertEquals("CALLE FALSA 123 COL CENTRO", documento.direccion)
    }

    /**
     * Si no se reconoce el formato es preferible mandar los campos vacíos que
     * mandar basura: la API interpreta un campo vacío como "no se pudo leer" y
     * conserva el valor anterior, mientras que un valor equivocado lo pisaría.
     */
    @Test
    fun `sin campos reconocibles manda nombre y direccion vacios`() {
        val documento = exito(codigo(curp))

        assertNull(documento.nombre)
        assertNull(documento.direccion)
    }

    @Test
    fun `calcula la edad a partir del curp`() {
        assertEquals(40, exito(codigo("0123|$curp")).edad)
    }

    // ------------------------------------------------------------- OCR INE ---

    @Test
    fun `lee una ine por ocr encontrando el curp impreso`() {
        val ocr = """
            INSTITUTO NACIONAL ELECTORAL
            CREDENCIAL PARA VOTAR
            NOMBRE
            PEREZ LOPEZ
            JUAN
            DOMICILIO
            CALLE FALSA 123 COL CENTRO
            06000 CIUDAD DE MEXICO
            CLAVE DE ELECTOR PRLPJN85031509H400
            CURP $curp
        """.trimIndent()

        val documento = exito(lector.leerOcrIne(ocr, 2025, 6, 1))

        assertEquals(curp, documento.identidad)
        assertEquals(OrigenDatos.OCR, documento.origen)
        assertEquals("PEREZ LOPEZ JUAN", documento.nombre)
        assertTrue(documento.direccion!!.startsWith("CALLE FALSA 123"))
    }

    @Test
    fun `el ocr sin curp no se da por bueno`() {
        val resultado = lector.leerOcrIne("INSTITUTO NACIONAL ELECTORAL\nJUAN PEREZ", 2025, 6, 1)

        assertTrue(resultado is ResultadoLectura.NoReconocido)
    }

    // ------------------------------------------------------- OCR pasaporte ---

    private val mrz = """
        P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<
        L898902C36UTO7408122F1204159ZE184226B<<<<<10
    """.trimIndent()

    @Test
    fun `lee un pasaporte por su mrz`() {
        val documento = exito(lector.leerOcrPasaporte("PASSPORT\nUTOPIA\n$mrz", 2025, 6, 1))

        assertEquals(TipoDocumento.PASAPORTE, documento.tipoDocumento)
        assertEquals("ANNA MARIA ERIKSSON", documento.nombre)
        assertEquals("UTO", documento.nacionalidad)
        assertEquals(50, documento.edad)
        assertTrue(documento.identidadConsistente)
    }

    /**
     * Un pasaporte no trae CURP y la API usa ese campo como llave, así que se
     * arma una clave determinista: el mismo pasaporte tiene que producir
     * siempre la misma, o el huésped recurrente se duplicaría.
     */
    @Test
    fun `arma una clave determinista para el pasaporte`() {
        val primera = exito(lector.leerOcrPasaporte(mrz, 2025, 6, 1)).identidad
        val segunda = exito(lector.leerOcrPasaporte(mrz, 2026, 1, 15)).identidad

        assertEquals("PAS-UTO-L898902C3", primera)
        assertEquals(primera, segunda)
    }

    @Test
    fun `la clave del pasaporte cabe en la columna de la base de datos`() {
        val clave = ClavePasaporte.generar("MEX", "G123456789012345")

        assertTrue("La columna CURP es nchar(20)", clave.length <= 20)
        assertTrue(ClavePasaporte.esClaveDePasaporte(clave))
    }

    @Test
    fun `marca como inconsistente un pasaporte con la mrz mal leida`() {
        val alterada = mrz.replace("L898902C36", "L898902C99")

        assertFalse(exito(lector.leerOcrPasaporte(alterada, 2025, 6, 1)).identidadConsistente)
    }

    @Test
    fun `un texto sin mrz no se da por bueno`() {
        val resultado = lector.leerOcrPasaporte("PASSPORT\nANNA MARIA ERIKSSON", 2025, 6, 1)

        assertTrue(resultado is ResultadoLectura.NoReconocido)
    }
}
