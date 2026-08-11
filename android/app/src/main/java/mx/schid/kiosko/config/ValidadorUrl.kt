package mx.schid.kiosko.config

/**
 * Valida la dirección del servidor que se captura en los ajustes.
 *
 * Vive aparte de [ConfiguracionKiosko] para poder probarla sin necesitar un
 * Context ni el almacén cifrado.
 */
object ValidadorUrl {

    /**
     * @param permiteHttp true solo en compilaciones de depuración. En release el
     *   token viajaría en claro por la red, y además el
     *   network_security_config bloquea el tráfico sin cifrar, así que una URL
     *   http fallaría de todos modos al conectar. Es preferible decirlo aquí,
     *   al configurar, que dejar que truene frente a un huésped.
     */
    fun validar(url: String, permiteHttp: Boolean): String? {
        val limpia = url.trim()

        return when {
            limpia.isBlank() ->
                "Escribe la dirección del servidor."

            limpia.startsWith("https://") ->
                null

            limpia.startsWith("http://") && permiteHttp ->
                null

            limpia.startsWith("http://") ->
                "Esta versión no acepta http://: el token viajaría en claro. Usa https:// " +
                    "o instala la compilación de depuración para probar."

            else ->
                "La dirección tiene que empezar con https:// (o http:// en depuración)."
        }
    }

    /** True si la dirección va sin cifrar, para poder avisarlo en pantalla. */
    fun esSinCifrar(url: String) = url.trim().startsWith("http://")
}
