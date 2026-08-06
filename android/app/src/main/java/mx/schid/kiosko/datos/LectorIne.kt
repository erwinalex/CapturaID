package mx.schid.kiosko.datos

/**
 * Datos que el kiosko manda a la API. Todos son opcionales salvo el CURP,
 * porque la API interpreta un campo vacío como "no se pudo leer" y conserva el
 * valor que ya tuviera — nunca lo borra.
 */
data class IneCapturada(
    val curp: String,
    val nombre: String? = null,
    val direccion: String? = null,
    val nacionalidad: String? = "MEXICANA",
    val edad: Int? = null,
    val residencia: String? = null,
    /** Falso si el dígito verificador del CURP no cuadró. Ver [Curp.digitoVerificadorCoincide]. */
    val curpConsistente: Boolean = true
)

sealed interface ResultadoLectura {
    data class Exito(val ine: IneCapturada) : ResultadoLectura

    /**
     * Se leyó un código de barras pero no se reconoció un CURP dentro. El
     * contenido crudo NO se incluye aquí a propósito: son datos personales y no
     * tienen por qué andar circulando en objetos de error o en logs. Para
     * mapear un formato desconocido está el modo diagnóstico, que es explícito
     * y solo lo abre quien tiene el PIN.
     */
    data class SinCurp(val largoContenido: Int) : ResultadoLectura
}

/**
 * Extrae los datos del código de barras del reverso de la INE.
 *
 * ## Lo que hay que verificar contra una credencial real
 *
 * El contenido exacto del PDF417 de la INE no está documentado públicamente y
 * cambia entre modelos de credencial (los modelos más nuevos traen además un QR
 * y, en algunos casos, contenido que no es texto plano). Por eso este lector no
 * asume posiciones fijas dentro de la cadena:
 *
 * - El **CURP se busca por forma**, no por posición: se recorren todas las
 *   secuencias de 18 caracteres del contenido y se toma la que cumple la
 *   estructura de un CURP. Eso funciona sin conocer el formato, siempre y
 *   cuando el CURP venga como texto.
 * - El **nombre y la dirección** sí dependen del formato, así que se intentan
 *   con los separadores más comunes y, si no se reconocen, simplemente se
 *   mandan vacíos. La API conserva los que ya tuviera y el operador puede
 *   completarlos después desde el PMS.
 *
 * Antes de poner esto en producción hay que escanear una credencial real con el
 * modo diagnóstico de la pantalla de configuración y confirmar qué trae. Si el
 * CURP no viene como texto plano, hay que resolverlo por OCR del frente o por
 * captura manual; ese caso está previsto en la interfaz pero no implementado.
 */
class LectorIne {

    private val separadores = listOf('|', '^', '\n', '\t', '~')

    fun leer(contenido: String, anio: Int, mes: Int, dia: Int): ResultadoLectura {
        val curp = Curp.buscarEn(contenido)
            ?: return ResultadoLectura.SinCurp(contenido.length)

        val campos = partirEnCampos(contenido)

        return ResultadoLectura.Exito(
            IneCapturada(
                curp = curp,
                nombre = adivinarNombre(campos, curp),
                direccion = adivinarDireccion(campos, curp),
                edad = Curp.edad(curp, anio, mes, dia),
                curpConsistente = Curp.digitoVerificadorCoincide(curp)
            )
        )
    }

    private fun partirEnCampos(contenido: String): List<String> {
        val separador = separadores.firstOrNull { contenido.contains(it) } ?: return emptyList()
        return contenido.split(separador).map { it.trim() }.filter { it.isNotEmpty() }
    }

    /**
     * El nombre es el campo con letras y espacios más largo que no sea el CURP.
     * Es una heurística, y por eso se descarta cualquier cosa que traiga dígitos
     * (una dirección casi siempre trae número).
     */
    private fun adivinarNombre(campos: List<String>, curp: String): String? {
        return campos
            .filter { it != curp && it.length in 6..200 }
            .filter { campo -> campo.none { it.isDigit() } }
            .filter { campo -> campo.any { it.isLetter() } }
            .maxByOrNull { it.length }
            ?.uppercase()
    }

    /**
     * La dirección se distingue del nombre en que mezcla letras y números
     * (calle y número, código postal).
     */
    private fun adivinarDireccion(campos: List<String>, curp: String): String? {
        return campos
            .filter { it != curp && it.length in 8..1000 }
            .filter { campo -> campo.any { it.isDigit() } && campo.any { it.isLetter() } }
            .filter { campo -> campo.count { it.isDigit() } < campo.length / 2 }
            .maxByOrNull { it.length }
            ?.uppercase()
    }
}
