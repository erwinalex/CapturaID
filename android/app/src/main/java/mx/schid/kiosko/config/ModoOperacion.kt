package mx.schid.kiosko.config

/**
 * Qué se le hace al dispositivo según el modo en que opere la app.
 *
 * ## Por qué es configurable
 *
 * En un mostrador con una tableta dedicada, el modo kiosko es lo correcto: el
 * huésped tiene el aparato en la mano y no debe poder salirse a curiosear. Pero
 * hay ubicaciones donde el mismo teléfono se usa para otras cosas, y ahí anclar
 * la app a la pantalla lo vuelve inservible para todo lo demás.
 *
 * ## Lo que NO depende del modo
 *
 * [bloquearCapturas] es true siempre. Protege los datos de la INE que están en
 * pantalla —impide capturas, grabación y la miniatura que Android guarda de la
 * app en la lista de recientes— y eso hace falta igual en los dos modos. De
 * hecho hace *más* falta fuera del modo kiosko: ahí el dispositivo se comparte
 * con otras aplicaciones y otras personas.
 *
 * Está aquí, y no dentro de un `if`, justamente para que no se pierda el día que
 * alguien decida simplificar "todo lo del kiosko" en una sola bandera.
 */
data class ModoOperacion(
    /** Anclar la app a la pantalla (screen pinning). */
    val anclarPantalla: Boolean,
    /** Impedir que la pantalla se apague sola. */
    val mantenerPantallaEncendida: Boolean,
    /** Ofrecerse como lanzador del dispositivo. */
    val puedeSerLanzador: Boolean,
    /** Impedir capturas de pantalla y miniaturas del sistema. */
    val bloquearCapturas: Boolean
) {
    companion object {
        fun para(modoKiosko: Boolean) = ModoOperacion(
            anclarPantalla = modoKiosko,
            mantenerPantallaEncendida = modoKiosko,
            puedeSerLanzador = modoKiosko,
            bloquearCapturas = true
        )
    }
}
