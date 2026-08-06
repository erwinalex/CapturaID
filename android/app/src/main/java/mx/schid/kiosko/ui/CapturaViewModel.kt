package mx.schid.kiosko.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import mx.schid.kiosko.config.ConfiguracionKiosko
import mx.schid.kiosko.datos.IneCapturada
import mx.schid.kiosko.datos.LectorIne
import mx.schid.kiosko.datos.ResultadoLectura
import mx.schid.kiosko.red.ResultadoEnvio
import mx.schid.kiosko.red.SchIdApi
import java.util.Calendar

enum class Paso {
    /** Pantalla de reposo. Nada del huésped anterior sigue en memoria. */
    INICIO,

    /** Pidiendo el frente de la credencial. */
    FRENTE,

    /** Pidiendo el reverso; aquí además se busca el código de barras. */
    REVERSO,

    ENVIANDO,

    LISTO,

    ERROR
}

data class EstadoCaptura(
    val paso: Paso = Paso.INICIO,
    val mensaje: String = "",
    /** Se muestra cuando el dígito verificador del CURP no cuadró. */
    val avisoCurp: Boolean = false,
    val permiteReintentar: Boolean = false
)

/**
 * Conduce la captura de una credencial.
 *
 * Regla que atraviesa todo este archivo: **nada de lo capturado sobrevive al
 * final del flujo**. El kiosko está en un mostrador a la vista de cualquiera y
 * no tiene por qué mostrar ni conservar datos de un huésped después de
 * mandarlos. Por eso [limpiar] borra los bytes de las fotos y el CURP en cuanto
 * termina el envío, y por eso ningún mensaje de la interfaz incluye el nombre o
 * el CURP de nadie.
 */
class CapturaViewModel(
    private val configuracion: ConfiguracionKiosko,
    private val lector: LectorIne = LectorIne()
) : ViewModel() {

    private val _estado = MutableStateFlow(EstadoCaptura())
    val estado: StateFlow<EstadoCaptura> = _estado.asStateFlow()

    private var frente: ByteArray? = null
    private var reverso: ByteArray? = null
    private var ine: IneCapturada? = null

    /** True mientras hay que seguir analizando fotogramas en busca del código. */
    val buscandoCodigo: Boolean
        get() = _estado.value.paso == Paso.REVERSO && ine == null

    fun comenzar() {
        limpiar()
        _estado.value = if (!configuracion.estaConfigurado) {
            EstadoCaptura(
                paso = Paso.ERROR,
                mensaje = "Este kiosko todavía no está configurado. Avisa al personal."
            )
        } else {
            EstadoCaptura(paso = Paso.FRENTE, mensaje = "Coloca el FRENTE de tu credencial")
        }
    }

    fun frenteCapturado(jpeg: ByteArray) {
        if (_estado.value.paso != Paso.FRENTE) return
        frente = jpeg
        _estado.value = EstadoCaptura(
            paso = Paso.REVERSO,
            mensaje = "Ahora voltéala y coloca el REVERSO"
        )
    }

    /**
     * Llega por cada fotograma en el que ML Kit reconoce un código de barras.
     * Se ignora todo lo que no traiga un CURP para que el operador pueda seguir
     * moviendo la credencial hasta que enfoque bien, en lugar de fallar al
     * primer intento.
     */
    fun codigoDetectado(contenido: String) {
        if (!buscandoCodigo) return

        val ahora = Calendar.getInstance()
        val lectura = lector.leer(
            contenido = contenido,
            anio = ahora.get(Calendar.YEAR),
            mes = ahora.get(Calendar.MONTH) + 1,
            dia = ahora.get(Calendar.DAY_OF_MONTH)
        )

        when (lectura) {
            is ResultadoLectura.Exito -> {
                ine = lectura.ine
                _estado.value = _estado.value.copy(
                    mensaje = "Credencial reconocida. No la muevas.",
                    avisoCurp = !lectura.ine.curpConsistente
                )
            }

            is ResultadoLectura.SinCurp -> {
                // Puede ser otro código impreso en la credencial: se sigue
                // buscando sin molestar al huésped.
            }
        }
    }

    fun reversoCapturado(jpeg: ByteArray) {
        if (_estado.value.paso != Paso.REVERSO) return
        reverso = jpeg

        val datos = ine
        if (datos == null) {
            _estado.value = EstadoCaptura(
                paso = Paso.ERROR,
                mensaje = "No se pudo leer el código del reverso. Acomódala e inténtalo de nuevo.",
                permiteReintentar = true
            )
            return
        }

        enviar(datos)
    }

    private fun enviar(datos: IneCapturada) {
        _estado.value = EstadoCaptura(paso = Paso.ENVIANDO, mensaje = "Enviando...")

        viewModelScope.launch {
            val api = SchIdApi(configuracion.urlBase, configuracion.token)
            val resultado = withContext(Dispatchers.IO) {
                api.registrar(datos, frente, reverso)
            }

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
                    mensaje = "La credencial no se pudo registrar. Avisa al personal.",
                    permiteReintentar = true
                )

                is ResultadoEnvio.FalloTemporal -> EstadoCaptura(
                    paso = Paso.ERROR,
                    mensaje = "No hay conexión con el servidor. Inténtalo de nuevo.",
                    permiteReintentar = true
                )
            }

            // Se borra pase lo que pase: si el envío falló, el huésped repite la
            // captura. Guardar sus fotos "por si acaso" sería acumular datos de
            // INE en el dispositivo, que es justo lo que este diseño evita.
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
        ine = null
    }

    override fun onCleared() {
        limpiar()
        super.onCleared()
    }
}
