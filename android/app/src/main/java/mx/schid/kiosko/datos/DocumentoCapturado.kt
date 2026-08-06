package mx.schid.kiosko.datos

enum class TipoDocumento { INE, PASAPORTE }

/**
 * De dónde salieron los datos. Se manda al log del kiosko (nunca con los
 * valores) para poder medir qué tan seguido hace falta bajar al siguiente
 * escalón: si la mayoría de las capturas terminan en MANUAL, algo se rompió en
 * la cámara o en el mapeo del código.
 */
enum class OrigenDatos { QR, PDF417, OCR, MANUAL }

/**
 * Lo que el kiosko manda a la API. Todos los campos salvo [identidad] son
 * opcionales, porque la API interpreta un campo vacío como "no se pudo leer" y
 * conserva el valor que ya tuviera — nunca lo borra.
 */
data class DocumentoCapturado(
    /**
     * Llave con la que la API decide si da de alta o actualiza. Para una INE es
     * el CURP; para un pasaporte, la clave sintética que arma
     * [ClavePasaporte.generar].
     */
    val identidad: String,
    val tipoDocumento: TipoDocumento,
    val origen: OrigenDatos,
    val nombre: String? = null,
    val direccion: String? = null,
    val nacionalidad: String? = null,
    val edad: Int? = null,
    val residencia: String? = null,
    /**
     * Falso cuando el dígito de control de la identidad no cuadró (el del CURP o
     * el de la MRZ). No impide registrar: se avisa en pantalla para que se
     * revise en recepción.
     */
    val identidadConsistente: Boolean = true
)

/**
 * Un pasaporte no trae CURP, y la API usa ese campo como llave para no duplicar
 * huéspedes. Se arma entonces una clave determinista a partir del país emisor y
 * el número de pasaporte, de forma que el mismo documento siempre produzca la
 * misma llave y el huésped recurrente se actualice en vez de duplicarse.
 *
 * El prefijo hace evidente en la base de datos que ese registro no es un CURP,
 * para que nadie intente validarlo como tal más adelante.
 *
 * OJO: la columna CURP de dbo.Personas es nchar(20). La clave más larga posible
 * aquí mide 16 (4 + 3 + 9), así que cabe, pero conviene tenerlo presente si
 * alguna vez se cambia el formato.
 */
object ClavePasaporte {

    const val PREFIJO = "PAS-"

    fun generar(paisEmisor: String, numero: String): String {
        val pais = paisEmisor.filter { it.isLetterOrDigit() }.uppercase().take(3)
        val limpio = numero.filter { it.isLetterOrDigit() }.uppercase().take(9)
        return "$PREFIJO$pais-$limpio"
    }

    fun esClaveDePasaporte(identidad: String) = identidad.startsWith(PREFIJO)
}
