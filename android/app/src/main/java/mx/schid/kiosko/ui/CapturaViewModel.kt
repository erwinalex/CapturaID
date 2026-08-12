package mx.schid.kiosko.ui

import android.util.Log
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import mx.schid.kiosko.config.AjustesServidor
import mx.schid.kiosko.datos.DocumentoCapturado
import mx.schid.kiosko.datos.LectorDocumentos
import mx.schid.kiosko.datos.OrigenDatos
import mx.schid.kiosko.datos.ResultadoLectura
import mx.schid.kiosko.datos.TipoDocumento
import mx.schid.kiosko.red.ResultadoEnvio
import mx.schid.kiosko.red.EnviadorRegistro
import java.util.Calendar

enum class Paso {
    /** Pantalla de reposo. Nada del huésped anterior sigue en memoria. */
    INICIO,

    /** Eligiendo entre credencial y pasaporte. */
    TIPO_DOCUMENTO,

    /** Frente de la INE, o página de datos del pasaporte. */
    FRENTE,

    /** Reverso de la INE; aquí además se buscan los códigos. */
    REVERSO,

    /** Intentando OCR sobre las fotos que ya se tomaron. */
    LEYENDO,

    /**
     * No se pudo leer el documento. Se ofrece repetir la foto antes de mandar a
     * teclear: la mayoría de las fallas del OCR son un reflejo o un desenfoque,
     * y repetir es mucho más rápido que capturar un CURP a mano.
     */
    NO_SE_PUDO_LEER,

    /**
     * Revisión de los datos antes de mandarlos. Llega con lo que se leyó del
     * documento, o vacío cuando ni el código ni el OCR sirvieron y le toca
     * capturar a una persona.
     */
    CONFIRMAR,

    ENVIANDO,

    LISTO,

    ERROR
}

data class EstadoCaptura(
    val paso: Paso = Paso.INICIO,
    val tipoDocumento: TipoDocumento = TipoDocumento.INE,
    val mensaje: String = "",
    /** Se muestra cuando el dígito de control de la identidad no cuadró. */
    val avisoIdentidad: Boolean = false,
    val permiteReintentar: Boolean = false,
    /**
     * Lo que se leyó del documento, para llenar la pantalla de confirmación.
     * Nulo cuando no se pudo leer nada y hay que capturar a mano. Se limpia al
     * terminar el flujo, como todo lo demás.
     */
    val prellenado: DocumentoCapturado? = null
)

/**
 * Conduce la captura de un documento.
 *
 * La lectura baja por tres escalones, en este orden: **código de barras (QR y
 * luego PDF417) → OCR de las fotos que ya se tomaron → captura manual**. Cada
 * escalón solo entra si el anterior no dio una identidad utilizable, así que en
 * el caso normal el huésped no nota que existen.
 *
 * Venga de donde venga, **nada se manda al servidor sin pasar por la pantalla
 * de confirmación**: el huésped ve lo que se leyó de su documento y lo corrige
 * si hace falta.
 *
 * Regla que atraviesa todo el archivo: **nada de lo capturado sobrevive al final
 * del flujo**. El kiosko está en un mostrador a la vista de cualquiera. Por eso
 * [limpiar] borra los bytes de las fotos y la identidad en cuanto termina el
 * envío, y por eso ningún mensaje de la interfaz incluye datos del huésped.
 */
class CapturaViewModel(
    private val ajustes: AjustesServidor,
    private val enviador: EnviadorRegistro,
    private val lector: LectorDocumentos = LectorDocumentos()
) : ViewModel() {

    private val _estado = MutableStateFlow(EstadoCaptura())
    val estado: StateFlow<EstadoCaptura> = _estado.asStateFlow()

    private var frente: ByteArray? = null
    private var reverso: ByteArray? = null
    private var documento: DocumentoCapturado? = null
    private var tipo: TipoDocumento = TipoDocumento.INE

    /** Función que corre OCR; la inyecta la pantalla, que es quien tiene cámara. */
    var reconocerTexto: ((ByteArray, (String?) -> Unit) -> Unit)? = null

    fun comenzar() {
        limpiar()
        _estado.value = if (!ajustes.estaConfigurado) {
            EstadoCaptura(
                paso = Paso.ERROR,
                mensaje = "Este kiosko todavía no está configurado. Avisa al personal."
            )
        } else {
            EstadoCaptura(paso = Paso.TIPO_DOCUMENTO)
        }
    }

    fun elegirTipo(tipoElegido: TipoDocumento) {
        tipo = tipoElegido
        _estado.value = EstadoCaptura(
            paso = Paso.FRENTE,
            tipoDocumento = tipoElegido,
            mensaje = when (tipoElegido) {
                TipoDocumento.INE -> "Coloca el FRENTE de tu credencial"
                TipoDocumento.PASAPORTE -> "Coloca la página de datos de tu pasaporte"
            }
        )
    }

    /**
     * Llega por cada fotograma donde ML Kit reconoce un código. Se ignora todo
     * lo que no traiga una identidad, para que el operador pueda seguir
     * acomodando el documento en lugar de fallar al primer intento.
     *
     * Un QR sí reemplaza a un PDF417 ya leído: es el escalón de más arriba.
     */
    fun codigoDetectado(codigo: CodigoLeido) {
        if (_estado.value.paso != Paso.REVERSO) return
        if (documento != null && codigo.origen != OrigenDatos.QR) return

        val ahora = Calendar.getInstance()
        val lectura = lector.leerCodigoIne(
            contenido = codigo.contenido,
            origen = codigo.origen,
            anio = ahora.get(Calendar.YEAR),
            mes = ahora.get(Calendar.MONTH) + 1,
            dia = ahora.get(Calendar.DAY_OF_MONTH)
        )

        if (lectura is ResultadoLectura.Exito) {
            documento = lectura.documento
            _estado.value = _estado.value.copy(
                mensaje = "Documento reconocido. No lo muevas.",
                avisoIdentidad = !lectura.documento.identidadConsistente
            )
        }
        // Si no trae identidad puede ser otro código impreso en el documento:
        // se sigue buscando sin molestar al huésped.
    }

    fun frenteCapturado(jpeg: ByteArray) {
        if (_estado.value.paso != Paso.FRENTE) return
        frente = jpeg

        if (tipo == TipoDocumento.PASAPORTE) {
            // El pasaporte es una sola página: no hay código que buscar, se pasa
            // directo al OCR de la MRZ.
            intentarOcr()
            return
        }

        _estado.value = _estado.value.copy(
            paso = Paso.REVERSO,
            mensaje = "Ahora voltéala y coloca el REVERSO"
        )
    }

    fun reversoCapturado(jpeg: ByteArray) {
        if (_estado.value.paso != Paso.REVERSO) return
        reverso = jpeg

        val leido = documento
        if (leido != null) {
            pedirConfirmacion(leido)
        } else {
            intentarOcr()
        }
    }

    /**
     * Segundo escalón. Corre OCR sobre las fotos que ya están tomadas, sin
     * pedirle nada nuevo al huésped.
     *
     * El frente va primero: es donde la INE imprime el nombre, el domicilio y el
     * CURP, y es también la única página del pasaporte. El reverso queda como
     * segundo intento porque ahí no hay más que los códigos de barras.
     */
    private fun intentarOcr() {
        val ocr = reconocerTexto
        if (ocr == null) {
            pedirCapturaManual()
            return
        }

        _estado.value = _estado.value.copy(paso = Paso.LEYENDO, mensaje = "Leyendo el documento...")

        val candidatos = listOfNotNull(frente, reverso)
        if (candidatos.isEmpty()) {
            pedirCapturaManual()
            return
        }

        intentarOcrEn(candidatos, 0, ocr)
    }

    private fun intentarOcrEn(
        imagenes: List<ByteArray>,
        indice: Int,
        ocr: (ByteArray, (String?) -> Unit) -> Unit
    ) {
        if (indice >= imagenes.size) {
            pedirCapturaManual()
            return
        }

        ocr(imagenes[indice]) { texto ->
            val leido = texto?.let { interpretarOcr(it) }
            if (leido != null) {
                documento = leido
                pedirConfirmacion(leido)
            } else {
                intentarOcrEn(imagenes, indice + 1, ocr)
            }
        }
    }

    private fun interpretarOcr(texto: String): DocumentoCapturado? {
        val ahora = Calendar.getInstance()
        val anio = ahora.get(Calendar.YEAR)
        val mes = ahora.get(Calendar.MONTH) + 1
        val dia = ahora.get(Calendar.DAY_OF_MONTH)

        val lectura = when (tipo) {
            TipoDocumento.INE -> lector.leerOcrIne(texto, anio, mes, dia)
            TipoDocumento.PASAPORTE -> lector.leerOcrPasaporte(texto, anio, mes, dia)
        }

        return (lectura as? ResultadoLectura.Exito)?.documento
    }

    /**
     * Lleva a la pantalla de confirmación con lo que se haya leído. Todo el
     * flujo desemboca aquí antes de mandar nada.
     */
    private fun pedirConfirmacion(leido: DocumentoCapturado) {
        _estado.value = _estado.value.copy(
            paso = Paso.CONFIRMAR,
            mensaje = "",
            avisoIdentidad = !leido.identidadConsistente,
            prellenado = leido
        )
    }

    /**
     * No salió nada del documento. Antes de mandar a teclear se ofrece repetir
     * la foto, porque casi siempre la causa es un reflejo o un desenfoque y
     * repetir cuesta segundos frente a capturar un CURP a mano.
     */
    private fun pedirCapturaManual() {
        _estado.value = _estado.value.copy(
            paso = Paso.NO_SE_PUDO_LEER,
            mensaje = "No se pudieron leer los datos. Acomoda el documento y evita reflejos.",
            avisoIdentidad = false,
            prellenado = null
        )
    }

    /** Repite la captura desde la primera foto, descartando las anteriores. */
    fun reintentarFotos() {
        limpiar()
        _estado.value = EstadoCaptura(
            paso = Paso.FRENTE,
            tipoDocumento = tipo,
            mensaje = when (tipo) {
                TipoDocumento.INE -> "Coloca el FRENTE de tu credencial"
                TipoDocumento.PASAPORTE -> "Coloca la página de datos de tu pasaporte"
            }
        )
    }

    /** Tercer escalón: que una persona capture los datos. */
    fun capturarAMano() {
        _estado.value = _estado.value.copy(
            paso = Paso.CONFIRMAR,
            mensaje = "",
            prellenado = null
        )
    }

    /** Lo que el huésped confirmó, ya con las correcciones que haya hecho. */
    fun confirmar(documentoConfirmado: DocumentoCapturado) {
        documento = documentoConfirmado
        enviar(documentoConfirmado)
    }

    fun cancelar() {
        volverAlInicio()
    }

    private fun enviar(datos: DocumentoCapturado) {
        _estado.value = _estado.value.copy(paso = Paso.ENVIANDO, mensaje = "Enviando...")

        viewModelScope.launch {
            val resultado = enviador.enviar(datos, frente, reverso)

            // Se registra de dónde salieron los datos, nunca los datos mismos.
            // Si la mayoría de las capturas terminan en MANUAL, algo se rompió
            // en la cámara o cambió el formato del documento.
            Log.i(ETIQUETA, "Captura ${datos.tipoDocumento} por ${datos.origen}: ${resultado::class.simpleName}")

            _estado.value = when (resultado) {
                is ResultadoEnvio.Exito -> EstadoCaptura(
                    paso = Paso.LISTO,
                    mensaje = "Listo. Puedes pasar a recepción."
                )

                is ResultadoEnvio.TokenRechazado -> EstadoCaptura(
                    paso = Paso.ERROR,
                    mensaje = "Este kiosko perdió su acceso al servidor. Avisa al personal."
                )

                is ResultadoEnvio.Rechazado -> EstadoCaptura(
                    paso = Paso.ERROR,
                    mensaje = "El documento no se pudo registrar. Avisa al personal.",
                    permiteReintentar = true
                )

                is ResultadoEnvio.FalloTemporal -> EstadoCaptura(
                    paso = Paso.ERROR,
                    mensaje = "No hay conexión con el servidor. Inténtalo de nuevo.",
                    permiteReintentar = true
                )
            }

            // Se borra pase lo que pase: si el envío falló, el huésped repite la
            // captura. Guardar sus fotos "por si acaso" sería acumular imágenes
            // de documentos de identidad en el dispositivo, que es justo lo que
            // este diseño evita.
            limpiar()
        }
    }

    fun volverAlInicio() {
        limpiar()
        _estado.value = EstadoCaptura(paso = Paso.INICIO)
    }

    private fun limpiar() {
        frente?.fill(0)
        reverso?.fill(0)
        frente = null
        reverso = null
        documento = null
    }

    override fun onCleared() {
        limpiar()
        super.onCleared()
    }

    private companion object {
        const val ETIQUETA = "SchIdKiosko"
    }
}
