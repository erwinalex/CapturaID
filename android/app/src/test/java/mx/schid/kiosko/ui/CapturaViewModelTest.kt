package mx.schid.kiosko.ui

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import mx.schid.kiosko.config.AjustesServidor
import mx.schid.kiosko.datos.DocumentoCapturado
import mx.schid.kiosko.datos.OrigenDatos
import mx.schid.kiosko.datos.TipoDocumento
import mx.schid.kiosko.red.EnviadorRegistro
import mx.schid.kiosko.red.RegistroExitoso
import mx.schid.kiosko.red.ResultadoEnvio
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

/**
 * Pruebas del flujo de captura.
 *
 * Existen por un motivo concreto: aquí se coló un error que ninguna prueba podía
 * ver. La lambda que recibía los códigos de barras quedaba congelada en la
 * primera composición y el escáner nunca llegaba a procesar nada, así que el
 * huésped acababa siempre en el formulario vacío. El flujo es la parte con más
 * estados y transiciones de toda la app, y era justo la que no estaba cubierta.
 */
@OptIn(ExperimentalCoroutinesApi::class)
class CapturaViewModelTest {

    private val dispatcher = StandardTestDispatcher()

    private val curp = "MELM850315HDFNPR07"
    private val jpeg = byteArrayOf(0xFF.toByte(), 0xD8.toByte(), 0xFF.toByte(), 0x01)

    private class AjustesDePrueba(
        override val urlBase: String = "https://servidor:7443",
        override val token: String = "token",
        override val estaConfigurado: Boolean = true
    ) : AjustesServidor

    private class EnviadorDePrueba(
        private val respuesta: ResultadoEnvio = ResultadoEnvio.Exito(
            RegistroExitoso(id = 1, resultado = "Creado", camposActualizados = emptyList())
        )
    ) : EnviadorRegistro {
        var enviados = mutableListOf<DocumentoCapturado>()

        override suspend fun enviar(
            documento: DocumentoCapturado,
            imagenFrente: ByteArray?,
            imagenReverso: ByteArray?
        ): ResultadoEnvio {
            enviados += documento
            return respuesta
        }
    }

    private lateinit var enviador: EnviadorDePrueba

    @Before
    fun antes() {
        Dispatchers.setMain(dispatcher)
        enviador = EnviadorDePrueba()
    }

    @After
    fun despues() {
        Dispatchers.resetMain()
    }

    private fun crear(ajustes: AjustesServidor = AjustesDePrueba()) =
        CapturaViewModel(ajustes, enviador)

    /** Lleva el flujo hasta tener el frente listo y esperando el reverso. */
    private fun CapturaViewModel.avanzarHastaReverso() {
        comenzar()
        elegirTipo(TipoDocumento.INE)
        frenteCapturado(jpeg)
    }

    // ------------------------------------------------------------ recorrido ---

    @Test
    fun `el flujo arranca pidiendo el tipo de documento`() {
        val vm = crear()
        vm.comenzar()

        assertEquals(Paso.TIPO_DOCUMENTO, vm.estado.value.paso)
    }

    @Test
    fun `sin configurar no deja capturar`() {
        val vm = crear(AjustesDePrueba(estaConfigurado = false))
        vm.comenzar()

        assertEquals(Paso.ERROR, vm.estado.value.paso)
    }

    @Test
    fun `la ine pide frente y luego reverso`() {
        val vm = crear()
        vm.comenzar()
        vm.elegirTipo(TipoDocumento.INE)
        assertEquals(Paso.FRENTE, vm.estado.value.paso)

        vm.frenteCapturado(jpeg)
        assertEquals(Paso.REVERSO, vm.estado.value.paso)
    }

    // ------------------------------------------- lo que el error dejó pasar ---

    /**
     * El caso que estaba roto: se lee un código y, al capturar el reverso, hay
     * que llegar a la confirmación con los datos ya puestos. Antes se caía al
     * OCR y de ahí al formulario vacío.
     */
    @Test
    fun `un codigo leido lleva a la confirmacion con los datos puestos`() {
        val vm = crear()
        vm.avanzarHastaReverso()

        vm.codigoDetectado(CodigoLeido("0123|JUAN PEREZ LOPEZ|$curp|CALLE FALSA 123", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        val estado = vm.estado.value
        assertEquals(Paso.CONFIRMAR, estado.paso)
        assertNotNull("Tiene que llegar prellenado con lo leído", estado.prellenado)
        assertEquals(curp, estado.prellenado!!.identidad)
        assertEquals("JUAN PEREZ LOPEZ", estado.prellenado.nombre)
        assertEquals(OrigenDatos.QR, estado.prellenado.origen)
    }

    /** Nada debe salir al servidor sin pasar antes por la confirmación. */
    @Test
    fun `leer un codigo no manda nada al servidor todavia`() {
        val vm = crear()
        vm.avanzarHastaReverso()

        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        assertTrue(enviador.enviados.isEmpty())
    }

    @Test
    fun `solo al confirmar se manda al servidor`() = runTest(dispatcher) {
        val vm = crear()
        vm.avanzarHastaReverso()
        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        vm.confirmar(vm.estado.value.prellenado!!)
        dispatcher.scheduler.advanceUntilIdle()

        assertEquals(1, enviador.enviados.size)
        assertEquals(curp, enviador.enviados.first().identidad)
        assertEquals(Paso.LISTO, vm.estado.value.paso)
    }

    /** Las correcciones del huésped son las que se mandan, no lo leído. */
    @Test
    fun `se manda lo corregido en la confirmacion`() = runTest(dispatcher) {
        val vm = crear()
        vm.avanzarHastaReverso()
        vm.codigoDetectado(CodigoLeido("0123|JUAN PEREZ|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        val corregido = vm.estado.value.prellenado!!.copy(nombre = "JUAN PEREZ LOPEZ")
        vm.confirmar(corregido)
        dispatcher.scheduler.advanceUntilIdle()

        assertEquals("JUAN PEREZ LOPEZ", enviador.enviados.first().nombre)
    }

    // --------------------------------------------------- prioridad de la ---
    //                                                      cadena de lectura

    @Test
    fun `un qr reemplaza a un pdf417 ya leido`() {
        val vm = crear()
        vm.avanzarHastaReverso()

        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.PDF417))
        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        assertEquals(OrigenDatos.QR, vm.estado.value.prellenado!!.origen)
    }

    @Test
    fun `un pdf417 no pisa a un qr ya leido`() {
        val vm = crear()
        vm.avanzarHastaReverso()

        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.QR))
        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.PDF417))
        vm.reversoCapturado(jpeg)

        assertEquals(OrigenDatos.QR, vm.estado.value.prellenado!!.origen)
    }

    @Test
    fun `un codigo sin curp se ignora y se sigue buscando`() {
        val vm = crear()
        vm.avanzarHastaReverso()

        vm.codigoDetectado(CodigoLeido("CODIGO|SIN|CURP", OrigenDatos.QR))

        assertEquals(Paso.REVERSO, vm.estado.value.paso)
        assertNull(vm.estado.value.prellenado)
    }

    // ------------------------------------------------- segundo y tercer paso ---

    @Test
    fun `sin codigo se intenta el ocr de las fotos ya tomadas`() {
        val vm = crear()
        vm.reconocerTexto = { _, alTerminar -> alTerminar("CURP $curp") }
        vm.avanzarHastaReverso()

        vm.reversoCapturado(jpeg)

        val estado = vm.estado.value
        assertEquals(Paso.CONFIRMAR, estado.paso)
        assertEquals(curp, estado.prellenado!!.identidad)
        assertEquals(OrigenDatos.OCR, estado.prellenado.origen)
    }

    /**
     * Cuando no se leyó nada, primero se ofrece repetir la foto: casi siempre la
     * causa es un reflejo o un desenfoque, y repetir cuesta segundos frente a
     * teclear un CURP a mano.
     */
    @Test
    fun `si el ocr tampoco sirve se ofrece repetir la foto`() {
        val vm = crear()
        vm.reconocerTexto = { _, alTerminar -> alTerminar("NADA UTIL AQUI") }
        vm.avanzarHastaReverso()

        vm.reversoCapturado(jpeg)

        assertEquals(Paso.NO_SE_PUDO_LEER, vm.estado.value.paso)
    }

    @Test
    fun `repetir la foto regresa al principio de la captura`() {
        val vm = crear()
        vm.reconocerTexto = { _, alTerminar -> alTerminar("NADA UTIL AQUI") }
        vm.avanzarHastaReverso()
        vm.reversoCapturado(jpeg)

        vm.reintentarFotos()

        assertEquals(Paso.FRENTE, vm.estado.value.paso)
        assertEquals(TipoDocumento.INE, vm.estado.value.tipoDocumento)
    }

    /** Y si repetir tampoco sirve, siempre queda teclear los datos. */
    @Test
    fun `capturar a mano lleva al formulario vacio`() {
        val vm = crear()
        vm.reconocerTexto = { _, alTerminar -> alTerminar("NADA UTIL AQUI") }
        vm.avanzarHastaReverso()
        vm.reversoCapturado(jpeg)

        vm.capturarAMano()

        assertEquals(Paso.CONFIRMAR, vm.estado.value.paso)
        assertNull("Sin datos que prellenar", vm.estado.value.prellenado)
    }

    /**
     * Tras repetir, la segunda vuelta tiene que poder leer con normalidad: no
     * puede quedar nada del intento fallido.
     */
    @Test
    fun `tras repetir la foto la lectura vuelve a funcionar`() {
        val vm = crear()
        vm.reconocerTexto = { _, alTerminar -> alTerminar("NADA UTIL AQUI") }
        vm.avanzarHastaReverso()
        vm.reversoCapturado(jpeg)
        vm.reintentarFotos()

        vm.frenteCapturado(jpeg)
        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        assertEquals(Paso.CONFIRMAR, vm.estado.value.paso)
        assertEquals(curp, vm.estado.value.prellenado!!.identidad)
    }

    @Test
    fun `el pasaporte va directo al ocr con una sola foto`() {
        val mrz = "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<\n" +
            "L898902C36UTO7408122F1204159ZE184226B<<<<<10"

        val vm = crear()
        vm.reconocerTexto = { _, alTerminar -> alTerminar(mrz) }
        vm.comenzar()
        vm.elegirTipo(TipoDocumento.PASAPORTE)

        vm.frenteCapturado(jpeg)

        val estado = vm.estado.value
        assertEquals(Paso.CONFIRMAR, estado.paso)
        assertEquals("PAS-UTO-L898902C3", estado.prellenado!!.identidad)
        assertEquals(TipoDocumento.PASAPORTE, estado.prellenado.tipoDocumento)
    }

    // ------------------------------------------------------------- desenlace ---

    @Test
    fun `un fallo de red deja reintentar`() = runTest(dispatcher) {
        enviador = EnviadorDePrueba(ResultadoEnvio.FalloTemporal("sin red"))
        val vm = crear()
        vm.avanzarHastaReverso()
        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        vm.confirmar(vm.estado.value.prellenado!!)
        dispatcher.scheduler.advanceUntilIdle()

        assertEquals(Paso.ERROR, vm.estado.value.paso)
        assertTrue(vm.estado.value.permiteReintentar)
    }

    /** Un token revocado no se arregla reintentando: hay que reconfigurar. */
    @Test
    fun `un token rechazado no ofrece reintentar`() = runTest(dispatcher) {
        enviador = EnviadorDePrueba(ResultadoEnvio.TokenRechazado)
        val vm = crear()
        vm.avanzarHastaReverso()
        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        vm.confirmar(vm.estado.value.prellenado!!)
        dispatcher.scheduler.advanceUntilIdle()

        assertEquals(Paso.ERROR, vm.estado.value.paso)
        assertFalse(vm.estado.value.permiteReintentar)
    }

    /**
     * Nada del huésped anterior puede quedar cuando el kiosko vuelve al reposo:
     * está en un mostrador a la vista de cualquiera.
     */
    @Test
    fun `al terminar no queda nada del huesped en el estado`() = runTest(dispatcher) {
        val vm = crear()
        vm.avanzarHastaReverso()
        vm.codigoDetectado(CodigoLeido("0123|JUAN PEREZ|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)
        vm.confirmar(vm.estado.value.prellenado!!)
        dispatcher.scheduler.advanceUntilIdle()

        vm.volverAlInicio()

        assertEquals(Paso.INICIO, vm.estado.value.paso)
        assertNull(vm.estado.value.prellenado)
    }

    @Test
    fun `cancelar la confirmacion regresa al inicio sin mandar nada`() {
        val vm = crear()
        vm.avanzarHastaReverso()
        vm.codigoDetectado(CodigoLeido("0123|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        vm.cancelar()

        assertEquals(Paso.INICIO, vm.estado.value.paso)
        assertNull(vm.estado.value.prellenado)
        assertTrue(enviador.enviados.isEmpty())
    }

    /**
     * El kiosko atiende a un huésped tras otro sin reiniciarse. Si algo del
     * ciclo anterior se quedara pegado, la segunda captura fallaría.
     */
    @Test
    fun `dos capturas seguidas funcionan igual`() = runTest(dispatcher) {
        val vm = crear()

        // Primera
        vm.avanzarHastaReverso()
        vm.codigoDetectado(CodigoLeido("0123|JUAN PEREZ|$curp", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)
        assertEquals(Paso.CONFIRMAR, vm.estado.value.paso)
        vm.confirmar(vm.estado.value.prellenado!!)
        dispatcher.scheduler.advanceUntilIdle()
        vm.volverAlInicio()

        // Segunda
        vm.avanzarHastaReverso()
        vm.codigoDetectado(CodigoLeido("0123|ANA GIL|MELM850315HDFNPR07", OrigenDatos.QR))
        vm.reversoCapturado(jpeg)

        assertEquals(Paso.CONFIRMAR, vm.estado.value.paso)
        assertNotNull("La segunda captura también debe prellenar", vm.estado.value.prellenado)
    }
}
