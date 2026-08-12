package mx.schid.kiosko.datos

/**
 * El CURP es la llave con la que la API decide si da de alta a alguien o
 * actualiza su registro. Si se manda uno mal leído se crea un huésped duplicado
 * que nadie va a notar hasta que estorbe, así que conviene validarlo aquí antes
 * de enviarlo.
 */
object Curp {

    private const val LARGO = 18

    /**
     * Entidades de nacimiento válidas. "NE" es "nacido en el extranjero".
     */
    private const val ENTIDADES =
        "AS|BC|BS|CC|CL|CM|CS|CH|DF|DG|GT|GR|HG|JC|MC|MN|MS|NT|NL|OC|PL|QT|QR|SL|SP|SR|TC|TL|TS|VZ|YN|ZS|NE"

    /**
     * Estructura de un CURP:
     * - 4 letras de apellidos y nombre (la 2a es vocal, o X cuando no hay)
     * - fecha de nacimiento AAMMDD
     * - H o M
     * - 2 letras de la entidad
     * - 3 consonantes internas
     * - homoclave (dígito si nació antes del 2000, letra si después)
     * - dígito verificador
     */
    private val ESTRUCTURA = Regex(
        "^[A-Z][AEIOUX][A-Z]{2}" +
            "\\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\\d|3[01])" +
            "[HM](?:$ENTIDADES)" +
            "[B-DF-HJ-NP-TV-Z]{3}" +
            "[0-9A-Z]\\d$"
    )

    /** Alfabeto con el que se calcula el dígito verificador. La Ñ vale 14. */
    private const val ALFABETO = "0123456789ABCDEFGHIJKLMNÑOPQRSTUVWXYZ"

    /**
     * Encuentra un CURP dentro de un texto cualquiera. Se usa para no depender
     * de en qué posición exacta venga dentro del código de barras: se busca por
     * forma, no por posición.
     */
    fun buscarEn(texto: String): String? {
        // Primero sobre el texto tal cual. Es lo que funciona con el contenido
        // de un código de barras, donde el CURP viene de corrido.
        buscarEnCadena(texto.uppercase())?.let { return it }

        // Y si ahí no salió, sobre el texto compactado. El OCR parte seguido el
        // CURP con espacios o saltos de línea ("MELM850315 HDFNPR07"), y así
        // ninguna ventana de 18 caracteres del texto original lo contiene
        // completo. Se deja como segundo intento y no como único, porque quitar
        // los separadores también podría pegar el final de un campo con el
        // principio del siguiente.
        val compactado = texto.uppercase().filter { it.isLetterOrDigit() }
        return buscarEnCadena(compactado)
    }

    /**
     * Recorre TODAS las ventanas de 18 caracteres, solapadas. Con una expresión
     * regular no alcanza: al buscar coincidencias de largo fijo no se solapan
     * entre sí, así que un CURP que no empiece justo donde cayó el corte
     * anterior queda invisible. Eso pasa exactamente en el caso que más importa
     * cubrir, el de un formato que no reconocemos y donde el CURP no viene
     * delimitado por separadores.
     */
    private fun buscarEnCadena(cadena: String): String? {
        if (cadena.length < LARGO) return null

        val candidatos = (0..(cadena.length - LARGO))
            .map { cadena.substring(it, it + LARGO) }
            .filter { tieneEstructuraValida(it) }

        // Si más de una ventana cumple la estructura, gana la que además cuadra
        // con su dígito verificador.
        return candidatos.firstOrNull { digitoVerificadorCoincide(it) }
            ?: candidatos.firstOrNull()
    }

    fun normalizar(valor: String): String = valor.trim().uppercase()

    fun tieneEstructuraValida(valor: String): Boolean {
        val curp = normalizar(valor)
        return curp.length == LARGO && ESTRUCTURA.matches(curp)
    }

    /**
     * Verifica el dígito de control (el carácter 18) contra los primeros 17.
     *
     * Deliberadamente NO se usa para rechazar una captura, solo como señal de
     * confianza: si el algoritmo tuviera un detalle mal, un CURP legítimo
     * quedaría bloqueado y el mostrador se detendría. Es preferible registrar al
     * huésped y que la incidencia quede marcada para revisarse.
     */
    fun digitoVerificadorCoincide(valor: String): Boolean {
        val curp = normalizar(valor)
        if (curp.length != LARGO) return false

        var suma = 0
        for (i in 0 until 17) {
            val posicion = ALFABETO.indexOf(curp[i])
            if (posicion < 0) return false
            suma += posicion * (18 - i)
        }

        val esperado = (10 - (suma % 10)) % 10
        return curp[17].digitToIntOrNull() == esperado
    }

    /**
     * Confusiones típicas del OCR. La estructura del CURP dice qué posiciones
     * son forzosamente dígitos y cuáles forzosamente letras, así que cada
     * carácter fuera de sitio se puede corregir sin adivinar.
     */
    private val COMO_DIGITO = mapOf(
        'O' to '0', 'Q' to '0', 'D' to '0', 'I' to '1', 'L' to '1',
        'S' to '5', 'B' to '8', 'G' to '6', 'Z' to '2'
    )

    private val COMO_LETRA = mapOf(
        '0' to 'O', '1' to 'I', '5' to 'S', '8' to 'B', '6' to 'G', '2' to 'Z'
    )

    /**
     * Corrige las confusiones del OCR usando la estructura del CURP.
     *
     * Nace de un caso real: sobre una credencial se leyó `...HDFRSRO5`, con
     * letra O donde va un cero. **El dígito de control no lo detecta** —la
     * diferencia entre O (25) y 0, multiplicada por el peso 2 de esa posición,
     * es múltiplo de 10— así que sin esta corrección el CURP equivocado se
     * habría mandado al servidor con toda apariencia de estar bien, y habría
     * creado un huésped duplicado el día que el OCR lo leyera correctamente.
     *
     * @param anioActual para decidir el siglo de nacimiento.
     * @return el CURP corregido, o null si ni así cumple la estructura.
     */
    fun corregirLecturaOcr(candidato: String, anioActual: Int): String? {
        val bruto = normalizar(candidato) ?: return null
        if (bruto.length != LARGO) return null

        val corregido = StringBuilder(bruto)

        // Fecha de nacimiento y dígito de control: solo dígitos.
        for (i in (4..9) + listOf(17)) {
            if (!corregido[i].isDigit()) {
                corregido[i] = COMO_DIGITO[corregido[i]] ?: corregido[i]
            }
        }

        // Iniciales, sexo, entidad y consonantes: solo letras.
        for (i in (0..3) + (10..15)) {
            if (!corregido[i].isLetter()) {
                corregido[i] = COMO_LETRA[corregido[i]] ?: corregido[i]
            }
        }

        // La homoclave es dígito para quienes nacieron antes del 2000 y letra
        // para después. Si leer el año como 20AA cayera en el futuro, la persona
        // nació en el siglo pasado y ahí va forzosamente un dígito.
        val anio2 = corregido.substring(4, 6).toIntOrNull()
        if (anio2 != null && 2000 + anio2 > anioActual && !corregido[16].isDigit()) {
            corregido[16] = COMO_DIGITO[corregido[16]] ?: corregido[16]
        }

        val resultado = corregido.toString()
        return if (tieneEstructuraValida(resultado)) resultado else null
    }

    /** Fecha de nacimiento en formato AAMMDD que trae el propio CURP. */
    fun fechaNacimiento(valor: String): String? {
        val curp = normalizar(valor)
        return if (curp.length >= 10) curp.substring(4, 10) else null
    }

    /**
     * Edad calculada a partir del CURP.
     *
     * El siglo no viene explícito: se deduce de la homoclave, que es un dígito
     * para quienes nacieron antes del 2000 y una letra para quienes nacieron
     * después. Es la misma convención que usa el RENAPO para no repetir claves.
     */
    fun edad(valor: String, anioActual: Int, mesActual: Int, diaActual: Int): Int? {
        val curp = normalizar(valor)
        if (!tieneEstructuraValida(curp)) return null

        val anio2 = curp.substring(4, 6).toIntOrNull() ?: return null
        val mes = curp.substring(6, 8).toIntOrNull() ?: return null
        val dia = curp.substring(8, 10).toIntOrNull() ?: return null

        val siglo = if (curp[16].isDigit()) 1900 else 2000
        val anio = siglo + anio2

        var edad = anioActual - anio
        if (mesActual < mes || (mesActual == mes && diaActual < dia)) {
            edad -= 1
        }

        return if (edad in 0..120) edad else null
    }
}
