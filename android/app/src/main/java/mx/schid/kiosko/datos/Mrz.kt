package mx.schid.kiosko.datos

/**
 * Lee la zona de lectura mecánica (MRZ) de un pasaporte: las dos líneas de 44
 * caracteres al pie de la página de datos, formato TD3 de la norma ICAO 9303.
 *
 * A diferencia del código de barras de la INE, este formato **sí está
 * especificado públicamente**, así que aquí sí se pueden usar posiciones fijas
 * con confianza. Además trae dígitos de control, lo que permite detectar un OCR
 * mal leído antes de mandar basura al servidor — algo que con la INE no se
 * puede hacer.
 *
 * Estructura de la segunda línea (posiciones desde 0):
 * ```
 *  0..8   número de pasaporte
 *  9      dígito de control del número
 * 10..12  nacionalidad
 * 13..18  fecha de nacimiento AAMMDD
 * 19      dígito de control de la fecha de nacimiento
 * 20      sexo
 * 21..26  fecha de vencimiento AAMMDD
 * 27      dígito de control del vencimiento
 * 28..41  número personal (opcional)
 * 42      dígito de control del número personal
 * 43      dígito de control compuesto
 * ```
 */
object Mrz {

    const val LARGO_LINEA = 44

    private val PESOS = intArrayOf(7, 3, 1)

    data class Pasaporte(
        val numero: String,
        val paisEmisor: String,
        val nacionalidad: String,
        val apellidos: String,
        val nombres: String,
        val fechaNacimiento: String,
        val sexo: String,
        val fechaVencimiento: String,
        /** True si todos los dígitos de control cuadraron. */
        val consistente: Boolean
    ) {
        val nombreCompleto: String
            get() = listOf(nombres, apellidos).filter { it.isNotBlank() }.joinToString(" ")
    }

    /**
     * Busca las dos líneas de la MRZ dentro de un texto que puede traer de todo
     * (lo que devuelve el OCR de la página completa).
     *
     * Se localizan por forma: dos renglones consecutivos de 44 caracteres del
     * alfabeto de la MRZ, el primero empezando con P (pasaporte). No se depende
     * de en qué renglón del OCR aparezcan.
     */
    fun buscarEn(texto: String): Pasaporte? {
        val renglones = texto
            .lineSequence()
            .map { normalizarRenglon(it) }
            .filter { it.length == LARGO_LINEA }
            .toList()

        for (i in 0 until renglones.size - 1) {
            if (!renglones[i].startsWith("P")) continue
            val pasaporte = leer(renglones[i], renglones[i + 1])
            if (pasaporte != null) return pasaporte
        }

        return null
    }

    /**
     * El OCR suele confundir el relleno '<' con '«', 'K' o '‹', y mete espacios
     * donde no hay. Se normaliza antes de medir el largo, porque si no, ninguna
     * línea daría los 44 exactos y la MRZ se descartaría entera.
     */
    fun normalizarRenglon(renglon: String): String {
        return renglon
            .uppercase()
            .replace('«', '<')
            .replace('‹', '<')
            .replace('“', '<')
            .replace(" ", "")
            .filter { it.isLetterOrDigit() || it == '<' }
    }

    fun leer(linea1: String, linea2: String): Pasaporte? {
        if (linea1.length != LARGO_LINEA || linea2.length != LARGO_LINEA) return null
        if (!linea1.startsWith("P")) return null

        val numero = linea2.substring(0, 9)
        val nacionalidad = linea2.substring(10, 13)
        val fechaNacimiento = linea2.substring(13, 19)
        val sexo = linea2.substring(20, 21)
        val fechaVencimiento = linea2.substring(21, 27)
        val numeroPersonal = linea2.substring(28, 42)

        val consistente =
            digitoDeControl(numero) == linea2[9].digitToIntOrNull() &&
                digitoDeControl(fechaNacimiento) == linea2[19].digitToIntOrNull() &&
                digitoDeControl(fechaVencimiento) == linea2[27].digitToIntOrNull() &&
                digitoDeControl(numeroPersonal) == linea2[42].digitToIntOrNull() &&
                digitoDeControl(compuesto(linea2)) == linea2[43].digitToIntOrNull()

        val paisEmisor = linea1.substring(2, 5)
        val (apellidos, nombres) = partirNombre(linea1.substring(5))

        return Pasaporte(
            numero = numero.trimEnd('<'),
            paisEmisor = paisEmisor.trimEnd('<'),
            nacionalidad = nacionalidad.trimEnd('<'),
            apellidos = apellidos,
            nombres = nombres,
            fechaNacimiento = fechaNacimiento,
            sexo = sexo,
            fechaVencimiento = fechaVencimiento,
            consistente = consistente
        )
    }

    /** Los tramos que entran en el dígito de control compuesto, según ICAO 9303. */
    private fun compuesto(linea2: String): String =
        linea2.substring(0, 10) + linea2.substring(13, 20) + linea2.substring(21, 43)

    /**
     * Dígito de control de ICAO 9303: se multiplica cada carácter por los pesos
     * 7, 3, 1 repetidos y se toma la suma módulo 10. Los dígitos valen su valor,
     * las letras 10 para la A hasta 35 para la Z, y el relleno '<' vale 0.
     */
    fun digitoDeControl(campo: String): Int {
        var suma = 0
        campo.forEachIndexed { indice, caracter ->
            val valor = when {
                caracter.isDigit() -> caracter - '0'
                caracter in 'A'..'Z' -> caracter - 'A' + 10
                caracter == '<' -> 0
                else -> return -1
            }
            suma += valor * PESOS[indice % PESOS.size]
        }
        return suma % 10
    }

    /** En la MRZ el nombre viene como APELLIDOS<<NOMBRES, con '<' por espacio. */
    private fun partirNombre(campo: String): Pair<String, String> {
        val partes = campo.split("<<", limit = 2)
        val apellidos = limpiar(partes.getOrElse(0) { "" })
        val nombres = limpiar(partes.getOrElse(1) { "" })
        return apellidos to nombres
    }

    private fun limpiar(valor: String) =
        valor.replace('<', ' ').trim().replace(Regex("\\s+"), " ")

    /**
     * Edad a partir de la fecha de nacimiento de la MRZ, que viene con dos
     * dígitos de año. El siglo se deduce comparando contra el año actual: una
     * fecha que quedaría en el futuro pertenece al siglo pasado.
     */
    fun edad(fechaNacimiento: String, anio: Int, mes: Int, dia: Int): Int? {
        if (fechaNacimiento.length != 6 || fechaNacimiento.any { !it.isDigit() }) return null

        val anio2 = fechaNacimiento.substring(0, 2).toInt()
        val mesNac = fechaNacimiento.substring(2, 4).toInt()
        val diaNac = fechaNacimiento.substring(4, 6).toInt()

        if (mesNac !in 1..12 || diaNac !in 1..31) return null

        val anioActual2 = anio % 100
        val siglo = if (anio2 > anioActual2) (anio / 100 - 1) * 100 else (anio / 100) * 100
        val anioNac = siglo + anio2

        var edad = anio - anioNac
        if (mes < mesNac || (mes == mesNac && dia < diaNac)) edad -= 1

        return if (edad in 0..120) edad else null
    }
}
