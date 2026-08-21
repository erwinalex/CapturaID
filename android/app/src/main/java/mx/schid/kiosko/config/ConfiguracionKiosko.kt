package mx.schid.kiosko.config

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import mx.schid.kiosko.BuildConfig

/**
 * URL del servidor, token y PIN de administración del kiosko.
 *
 * Se guardan cifrados (respaldados por el keystore del dispositivo) y no se
 * compilan dentro del APK. Hornear el token en BuildConfig sería más simple,
 * pero obligaría a recompilar y reinstalar en cada equipo para rotarlo — y
 * rotarlo es justo lo que hay que poder hacer rápido el día que se extravíe una
 * tableta.
 */
class ConfiguracionKiosko(context: Context) : AjustesServidor {

    private val prefs: SharedPreferences = EncryptedSharedPreferences.create(
        context,
        "schid-kiosko",
        MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build(),
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    override var direccionServidor: String
        get() = prefs.getString(CLAVE_URL, "").orEmpty()
        set(valor) = prefs.edit().putString(CLAVE_URL, valor.trim()).apply()

    override var token: String
        get() = prefs.getString(CLAVE_TOKEN, "").orEmpty()
        set(valor) = prefs.edit().putString(CLAVE_TOKEN, valor.trim()).apply()

    /**
     * PIN para abrir la pantalla de configuración. Con la app anclada a la
     * pantalla (screen pinning), esta es la única puerta hacia los ajustes.
     */
    var pinAdministracion: String
        get() = prefs.getString(CLAVE_PIN, PIN_INICIAL).orEmpty()
        set(valor) = prefs.edit().putString(CLAVE_PIN, valor).apply()

    /**
     * Si la app se apodera del dispositivo (ver [ModoOperacion]).
     *
     * Por omisión **sí**: es lo que hacía la app antes de que esto fuera
     * configurable, y un kiosko ya instalado no debe quedar desanclado por
     * actualizarla. Quien quiera usar el teléfono para otras cosas lo apaga a
     * propósito desde esta pantalla.
     */
    var modoKiosko: Boolean
        get() = prefs.getBoolean(CLAVE_MODO_KIOSKO, true)
        set(valor) = prefs.edit().putBoolean(CLAVE_MODO_KIOSKO, valor).apply()

    val modoOperacion: ModoOperacion
        get() = ModoOperacion.para(modoKiosko)

    override val estaConfigurado: Boolean
        get() = direccionServidor.isNotBlank() && token.isNotBlank()

    /**
     * Si sigue el PIN de fábrica, cualquiera que sepa que este software existe
     * puede entrar a los ajustes del kiosko. La pantalla de configuración avisa.
     */
    val usaPinDeFabrica: Boolean
        get() = pinAdministracion == PIN_INICIAL

    /**
     * Ver [DireccionServidor]. Se acepta http únicamente en compilaciones de
     * depuración, para poder probar contra un servidor que todavía no tiene
     * certificado; el APK de release lo rechaza.
     */
    fun validarDireccion(texto: String): String? =
        DireccionServidor.validar(texto, permiteHttp = BuildConfig.DEBUG)

    private companion object {
        const val CLAVE_URL = "url_base"
        const val CLAVE_TOKEN = "token"
        const val CLAVE_PIN = "pin_admin"
        const val CLAVE_MODO_KIOSKO = "modo_kiosko"
        const val PIN_INICIAL = "0000"
    }
}
