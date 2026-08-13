package mx.schid.kiosko.datos

import kotlin.math.roundToInt

/** Un rectángulo en píxeles, sin depender de android.graphics para poder probarlo. */
data class Recorte(val x: Int, val y: Int, val ancho: Int, val alto: Int)

/**
 * Calcula la región de la foto que contiene la credencial.
 *
 * La cámara captura todo lo que ve —el mostrador, la mano, lo que haya detrás— y
 * dependiendo del formato del sensor puede acabar habiendo más fondo que
 * documento. Guardar eso significa más espacio del necesario, un OCR con más
 * ruido del necesario, y datos del entorno que nadie pidió conservar.
 *
 * La solución es una guía en pantalla con la proporción de una credencial: el
 * huésped la acomoda dentro y se recorta exactamente esa región. **El mismo
 * cálculo alimenta la guía que se dibuja y el recorte que se aplica**, que es lo
 * que impide que se desalineen — si fueran dos números distintos, cualquier
 * ajuste en uno dejaría al otro mintiendo.
 */
object RecorteCredencial {

    /**
     * Proporción de una credencial ID-1 (norma ISO/IEC 7810): 85.60 × 53.98 mm.
     * Es la de la INE, y también la de una tarjeta bancaria.
     */
    const val PROPORCION = 85.60f / 53.98f

    /**
     * Qué parte del ancho ocupa la guía. Se deja margen a propósito: el huésped
     * acomoda el documento a ojo, y es preferible guardar un poco de fondo a
     * cortarle el borde a la credencial.
     */
    const val ANCHO_RELATIVO = 0.92f

    /** No se ocupa más de esta parte del alto, para que la guía no toque los bordes. */
    private const val ALTO_MAXIMO_RELATIVO = 0.9f

    /**
     * Región de la credencial dentro de una imagen ya orientada (o sea, con el
     * documento en horizontal y la imagen en vertical, como la ve el huésped).
     */
    fun calcular(ancho: Int, alto: Int): Recorte {
        var anchoGuia = ancho * ANCHO_RELATIVO
        var altoGuia = anchoGuia / PROPORCION

        // En una imagen muy apaisada la guía no cabría de alto; ahí manda el alto.
        val altoMaximo = alto * ALTO_MAXIMO_RELATIVO
        if (altoGuia > altoMaximo) {
            altoGuia = altoMaximo
            anchoGuia = altoGuia * PROPORCION
        }

        val anchoFinal = anchoGuia.roundToInt().coerceAtLeast(1).coerceAtMost(ancho)
        val altoFinal = altoGuia.roundToInt().coerceAtLeast(1).coerceAtMost(alto)

        return Recorte(
            x = ((ancho - anchoFinal) / 2f).roundToInt().coerceAtLeast(0),
            y = ((alto - altoFinal) / 2f).roundToInt().coerceAtLeast(0),
            ancho = anchoFinal,
            alto = altoFinal
        )
    }
}
