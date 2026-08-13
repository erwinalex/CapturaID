package mx.schid.kiosko.config

/**
 * Cómo se alcanza el servidor de una ubicación.
 *
 * ## El problema que resuelve
 *
 * Un certificado TLS vale para los nombres y las IPs que lleva en su SAN, y
 * Android exige que la dirección con la que conectas esté ahí. Atar el
 * certificado a la IP significaría un certificado distinto por ubicación —y, en
 * las que no tienen IP fija, un certificado que deja de servir el día que el
 * router reparte otra dirección.
 *
 * La salida es no conectar nunca por IP. La app pide siempre
 * `https://[NOMBRE_CERTIFICADO]:puerto`, y **resuelve ese nombre por su cuenta**
 * hacia la IP que se haya configurado en el kiosko (ver el `Dns` de
 * [mx.schid.kiosko.red.SchIdApi]). Para TLS el nombre es siempre el mismo, así
 * que un solo certificado sirve para las 70 ubicaciones y una sola compilación
 * de la app sirve para todas; y cuando una IP cambia, se corrige en los ajustes
 * del kiosko sin tocar certificados ni recompilar nada.
 *
 * La validación del certificado no se toca: sigue exigiéndose que lo firme
 * nuestra CA y que el nombre coincida. Lo único que se sustituye es el paso de
 * resolución de nombres, que es lo que en un servidor doméstico no existe.
 */
object DireccionServidor {

    /**
     * El nombre que lleva el certificado. Es el mismo en todas las ubicaciones,
     * y por eso puede haber un solo certificado y un solo APK.
     */
    const val NOMBRE_CERTIFICADO = "schid-servidor"

    const val PUERTO_POR_OMISION = 7443

    data class Destino(
        /** IP o nombre real de la máquina en esa ubicación. */
        val host: String,
        val puerto: Int,
        val conTls: Boolean
    ) {
        /**
         * La URL que se pide. Con TLS es siempre el nombre del certificado —la
         * IP se aplica al resolver— y sin TLS se va directo al host, porque ahí
         * no hay certificado que validar.
         */
        val url: String
            get() = if (conTls) "https://$NOMBRE_CERTIFICADO:$puerto" else "http://$host:$puerto"
    }

    /**
     * Interpreta lo que se capturó en los ajustes. Acepta:
     * - `192.168.1.226` (https en el puerto por omisión)
     * - `192.168.1.226:7443`
     * - `servidor-hotel:7443`
     * - `http://192.168.1.226:5080` (solo compilaciones de depuración)
     */
    fun interpretar(texto: String): Destino? {
        var resto = texto.trim()
        if (resto.isEmpty()) return null

        var conTls = true
        when {
            resto.startsWith("https://", ignoreCase = true) -> resto = resto.removePrefix("https://").removePrefix("HTTPS://")
            resto.startsWith("http://", ignoreCase = true) -> {
                conTls = false
                resto = resto.substring("http://".length)
            }
        }

        resto = resto.trimEnd('/')
        if (resto.isEmpty()) return null

        val partes = resto.split(":")
        if (partes.size > 2) return null

        val host = partes[0].trim()
        if (host.isEmpty() || host.any { it.isWhitespace() }) return null

        val puerto = if (partes.size == 2) {
            partes[1].trim().toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
        } else if (conTls) {
            PUERTO_POR_OMISION
        } else {
            80
        }

        return Destino(host, puerto, conTls)
    }

    /**
     * @param permiteHttp true solo en compilaciones de depuración. En release el
     *   token viajaría en claro y el network_security_config bloquea el tráfico
     *   sin cifrar, así que fallaría igual — mejor decirlo al configurar.
     */
    fun validar(texto: String, permiteHttp: Boolean): String? {
        if (texto.isBlank()) {
            return "Escribe la dirección del servidor, por ejemplo 192.168.1.226:7443"
        }

        val destino = interpretar(texto)
            ?: return "No se entiende la dirección. Usa algo como 192.168.1.226:7443"

        if (!destino.conTls && !permiteHttp) {
            return "Esta versión no acepta http://: el token viajaría en claro. " +
                "Quita el http:// o instala la compilación de depuración para probar."
        }

        return null
    }

    fun esSinCifrar(texto: String) = interpretar(texto)?.conTls == false
}
