package mx.schid.kiosko.datos

/**
 * Resultado de intentar leer un documento con una de las estrategias.
 */
sealed interface ResultadoLectura {
    data class Exito(val documento: DocumentoCapturado) : ResultadoLectura

    /**
     * No se reconoció la identidad. El contenido crudo NO viaja aquí a
     * propósito: son datos personales y no tienen por qué circular en objetos
     * de error ni acabar en un log. Solo se reporta cuánto medía, que es lo
     * único útil para diagnosticar.
     */
    data class NoReconocido(val largoContenido: Int) : ResultadoLectura
}

/**
 * Las tres formas de sacar los datos de un documento, en el orden en que se
 * intentan:
 *
 * 1. **Código de barras** (QR primero, PDF417 después). Es lo más confiable
 *    cuando funciona: no hay interpretación de por medio.
 * 2. **OCR** de las fotos que ya se tomaron. Entra cuando el código está
 *    rayado, borroso o el modelo de credencial no lo trae legible.
 * 3. **Captura manual**, que no vive aquí sino en la interfaz.
 *
 * ## Lo que hay que verificar contra documentos reales
 *
 * El contenido del QR y del PDF417 de la INE **no está documentado
 * públicamente** y cambia entre modelos de credencial. Por eso el lector no
 * asume posiciones fijas: busca el CURP por su forma, recorriendo el contenido.
 * Eso funciona sin conocer el formato, siempre que el CURP venga como texto.
 *
 * La MRZ del pasaporte es el caso opuesto: está especificada en ICAO 9303, trae
 * dígitos de control y sí se lee por posiciones. Ver [Mrz].
 */
class LectorDocumentos {

    private val separadores = listOf('|', '^', '\n', '\t', '~')

    /**
     * Etiquetas impresas en el frente de la INE. Están confirmadas contra
     * credenciales reales: el dato viene siempre justo después de su etiqueta,
     * lo que las vuelve un ancla más fiable que cualquier heurística de forma.
     */
    private val etiquetasNombre = listOf("NOMBRE")
    private val etiquetasDomicilio = listOf("DOMICILIO")
    private val etiquetaCurp = "CURP"

    /**
     * Todas las etiquetas del frente. Sirven para saber dónde termina un dato:
     * al toparse con la siguiente etiqueta, el valor anterior se acabó.
     */
    private val todasLasEtiquetas = listOf(
        "NOMBRE", "DOMICILIO", "CLAVE DE ELECTOR", "CURP", "FECHA DE NACIMIENTO",
        "SEXO", "ESTADO", "MUNICIPIO", "LOCALIDAD", "SECCION", "SECCIÓN",
        "AÑO DE REGISTRO", "ANO DE REGISTRO", "VIGENCIA", "EMISION", "EMISIÓN",
        "INSTITUTO NACIONAL ELECTORAL", "CREDENCIAL PARA VOTAR", "REGISTRO"
    )

    // ---------------------------------------------------------------- INE ---

    /**
     * Lee el contenido de un código de barras de la INE. Sirve igual para QR y
     * para PDF417: en ambos se busca el CURP por forma, y el formato solo
     * cambia de dónde vino, que se registra en [DocumentoCapturado.origen].
     */
    fun leerCodigoIne(
        contenido: String,
        origen: OrigenDatos,
        anio: Int,
        mes: Int,
        dia: Int
    ): ResultadoLectura {
        val curp = Curp.buscarEn(contenido)
            ?: return ResultadoLectura.NoReconocido(contenido.length)

        val campos = partirEnCampos(contenido)

        return ResultadoLectura.Exito(
            DocumentoCapturado(
                identidad = curp,
                tipoDocumento = TipoDocumento.INE,
                origen = origen,
                nombre = adivinarNombre(campos, curp),
                direccion = adivinarDireccion(campos, curp),
                nacionalidad = "MEXICANA",
                edad = Curp.edad(curp, anio, mes, dia),
                identidadConsistente = Curp.digitoVerificadorCoincide(curp)
            )
        )
    }

    /**
     * Lee una INE a partir del texto que devolvió el OCR de las fotos.
     *
     * El CURP viene impreso como texto en el frente de la credencial, así que la
     * misma búsqueda por forma que se usa con el código de barras funciona aquí
     * sin cambios.
     */
    fun leerOcrIne(texto: String, anio: Int, mes: Int, dia: Int): ResultadoLectura {
        // Primero anclado a la etiqueta, que en todas las credenciales aparece
        // justo antes del dato; si de ahí no sale un CURP válido, se busca por
        // forma en todo el texto. El ancla evita confundirse con la clave de
        // elector, que también son 18 caracteres alfanuméricos.
        val curp = curpTrasEtiqueta(texto)
            ?: Curp.buscarEn(texto)
            ?: return ResultadoLectura.NoReconocido(texto.length)

        val renglones = texto.lines().map { it.trim() }.filter { it.isNotEmpty() }

        return ResultadoLectura.Exito(
            DocumentoCapturado(
                identidad = curp,
                tipoDocumento = TipoDocumento.INE,
                origen = OrigenDatos.OCR,
                // Tres renglones para el nombre: la INE imprime apellido
                // paterno, materno y nombres en líneas separadas bajo la misma
                // etiqueta.
                nombre = valorTrasEtiqueta(renglones, etiquetasNombre, maxRenglones = 3),
                direccion = valorTrasEtiqueta(renglones, etiquetasDomicilio, maxRenglones = 3),
                nacionalidad = "MEXICANA",
                edad = Curp.edad(curp, anio, mes, dia),
                identidadConsistente = Curp.digitoVerificadorCoincide(curp)
            )
        )
    }

    // ---------------------------------------------------------- PASAPORTE ---

    /**
     * Lee un pasaporte a partir del OCR de la página de datos, localizando su
     * MRZ. Como un pasaporte no trae CURP y la API usa ese campo como llave, se
     * arma una clave determinista con el país emisor y el número —
     * ver [ClavePasaporte].
     */
    fun leerOcrPasaporte(texto: String, anio: Int, mes: Int, dia: Int): ResultadoLectura {
        val pasaporte = Mrz.buscarEn(texto)
            ?: return ResultadoLectura.NoReconocido(texto.length)

        return ResultadoLectura.Exito(
            DocumentoCapturado(
                identidad = ClavePasaporte.generar(pasaporte.paisEmisor, pasaporte.numero),
                tipoDocumento = TipoDocumento.PASAPORTE,
                origen = OrigenDatos.OCR,
                nombre = pasaporte.nombreCompleto.ifBlank { null },
                nacionalidad = pasaporte.nacionalidad.ifBlank { null },
                edad = Mrz.edad(pasaporte.fechaNacimiento, anio, mes, dia),
                identidadConsistente = pasaporte.consistente
            )
        )
    }

    // ------------------------------------------------------------ COMUNES ---

    private fun partirEnCampos(contenido: String): List<String> {
        val separador = separadores.firstOrNull { contenido.contains(it) } ?: return emptyList()
        return contenido.split(separador).map { it.trim() }.filter { it.isNotEmpty() }
    }

    /**
     * El nombre es el campo de puras letras más largo que no sea el CURP. Es una
     * heurística: se descarta cualquier cosa con dígitos, porque una dirección
     * casi siempre trae número.
     */
    private fun adivinarNombre(campos: List<String>, curp: String): String? =
        campos
            .filter { it != curp && it.length in 6..200 }
            .filter { campo -> campo.none { it.isDigit() } }
            .filter { campo -> campo.any { it.isLetter() } }
            .maxByOrNull { it.length }
            ?.uppercase()

    /** La dirección se distingue del nombre en que mezcla letras y números. */
    private fun adivinarDireccion(campos: List<String>, curp: String): String? =
        campos
            .filter { it != curp && it.length in 8..1000 }
            .filter { campo -> campo.any { it.isDigit() } && campo.any { it.isLetter() } }
            .filter { campo -> campo.count { it.isDigit() } < campo.length / 2 }
            .maxByOrNull { it.length }
            ?.uppercase()

    /**
     * En la INE el dato va debajo de su etiqueta ("NOMBRE", "DOMICILIO"), y el
     * OCR entrega cada renglón por separado. Se toman los renglones siguientes
     * hasta toparse con otra etiqueta.
     */
    private fun valorTrasEtiqueta(
        renglones: List<String>,
        etiquetas: List<String>,
        maxRenglones: Int
    ): String? {
        val indice = renglones.indexOfFirst { renglon ->
            etiquetas.any { renglon.uppercase().startsWith(it) }
        }
        if (indice < 0) return null

        // A veces el OCR deja el valor en el mismo renglón que la etiqueta
        // ("NOMBRE JUAN PEREZ") y a veces lo baja al siguiente. Se contemplan
        // los dos casos.
        val mismoRenglon = renglones[indice].uppercase()
            .removePrefix(etiquetas.first { renglones[indice].uppercase().startsWith(it) })
            .trim()

        val siguientes = renglones
            .drop(indice + 1)
            .takeWhile { renglon -> todasLasEtiquetas.none { renglon.uppercase().startsWith(it) } }
            .take(maxRenglones)

        val valor = (listOf(mismoRenglon) + siguientes)
            .filter { it.isNotBlank() }
            .joinToString(" ")
            .trim()

        return valor.ifBlank { null }?.uppercase()
    }

    /**
     * Toma los 18 caracteres alfanuméricos que siguen a la etiqueta "CURP",
     * saltándose espacios y saltos de línea, y solo lo devuelve si cumple la
     * estructura oficial. Si el OCR leyó mal un carácter, esto falla y se cae a
     * la búsqueda por forma — es preferible eso a devolver un CURP inventado,
     * que crearía un huésped duplicado.
     */
    private fun curpTrasEtiqueta(texto: String): String? {
        val mayusculas = texto.uppercase()
        val posicion = mayusculas.indexOf(etiquetaCurp)
        if (posicion < 0) return null

        val despues = mayusculas
            .substring(posicion + etiquetaCurp.length)
            .filter { it.isLetterOrDigit() }

        if (despues.length < 18) return null

        val candidato = despues.take(18)
        return if (Curp.tieneEstructuraValida(candidato)) candidato else null
    }
}
