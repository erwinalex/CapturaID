package mx.schid.kiosko

import android.Manifest
import android.content.ComponentName
import android.content.pm.PackageManager
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.core.content.ContextCompat
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.compose.viewModel
import mx.schid.kiosko.config.ConfiguracionKiosko
import mx.schid.kiosko.red.EnviadorHttp
import mx.schid.kiosko.ui.CapturaViewModel
import mx.schid.kiosko.ui.PantallaCaptura
import mx.schid.kiosko.ui.PantallaConfiguracion
import mx.schid.kiosko.ui.PantallaDiagnostico

class MainActivity : ComponentActivity() {

    private lateinit var configuracion: ConfiguracionKiosko

    private val pedirCamara = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { concedido -> hayCamara = concedido }

    private var hayCamara by mutableStateOf(false)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        configuracion = ConfiguracionKiosko(applicationContext)

        // FLAG_SECURE impide capturas de pantalla, grabación y la miniatura que
        // Android guarda de la app en la lista de recientes. En esta pantalla se
        // ve una credencial de elector, así que esa miniatura sería una copia de
        // la INE guardada por el sistema sin que nadie lo pidiera.
        //
        // No depende del modo: fuera del kiosko hace todavía más falta, porque
        // el dispositivo se comparte con otras apps.
        window.setFlags(WindowManager.LayoutParams.FLAG_SECURE, WindowManager.LayoutParams.FLAG_SECURE)

        aplicarModoOperacion()

        hayCamara = ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) ==
            PackageManager.PERMISSION_GRANTED

        if (!hayCamara) {
            pedirCamara.launch(Manifest.permission.CAMERA)
        }

        setContent {
            MaterialTheme(colorScheme = darkColorScheme()) {
                Surface(modifier = Modifier.fillMaxSize()) {
                    var enConfiguracion by remember { mutableStateOf(false) }
                    var enDiagnostico by remember { mutableStateOf(false) }

                    when {
                        enDiagnostico -> PantallaDiagnostico(
                            alSalir = { enDiagnostico = false }
                        )

                        enConfiguracion -> PantallaConfiguracion(
                            configuracion = configuracion,
                            alAbrirDiagnostico = { enDiagnostico = true },
                            alSalir = {
                                enConfiguracion = false
                                // El modo pudo haber cambiado ahí dentro.
                                aplicarModoOperacion()
                            }
                        )

                        !hayCamara -> Text(
                            "El kiosko necesita permiso de cámara para funcionar. " +
                                "Concédelo desde los ajustes del dispositivo."
                        )

                        else -> {
                            val viewModel: CapturaViewModel = viewModel(
                                factory = FabricaViewModel(configuracion)
                            )
                            PantallaCaptura(
                                viewModel = viewModel,
                                alPedirConfiguracion = { enConfiguracion = true }
                            )
                        }
                    }
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        aplicarModoOperacion()
    }

    /**
     * Pone el dispositivo como pida el modo configurado.
     *
     * Se llama al arrancar, al volver a primer plano y al salir de los ajustes,
     * porque el modo se cambia desde dentro de la propia app: si solo se
     * aplicara en onCreate, apagar el modo kiosko no surtiría efecto hasta
     * reiniciar la app — y estando anclada, reiniciarla es justo lo que no se
     * puede hacer.
     */
    fun aplicarModoOperacion() {
        val modo = configuracion.modoOperacion

        if (modo.mantenerPantallaEncendida) {
            // Un kiosko que se apaga solo obliga a alguien a ir a despertarlo.
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }

        // Sin un MDM que ponga la app como device owner, Android pide
        // confirmación al usuario la primera vez que se ancla; con device owner
        // queda anclada de forma permanente.
        //
        // stopLockTask importa tanto como startLockTask: es lo que hace que
        // apagar el modo kiosko libere el dispositivo en el momento, sin tener
        // que desinstalar la app para recuperarlo.
        runCatching {
            if (modo.anclarPantalla) startLockTask() else stopLockTask()
        }

        habilitarComponente(LANZADOR, modo.puedeSerLanzador)
    }

    /**
     * Enciende o apaga el alias que ofrece la app como lanzador del
     * dispositivo. DONT_KILL_APP evita que Android reinicie el proceso al
     * cambiarlo, que estando a mitad de una captura se perdería lo capturado.
     */
    private fun habilitarComponente(clase: String, habilitado: Boolean) {
        val componente = ComponentName(packageName, clase)
        val estado = if (habilitado) {
            PackageManager.COMPONENT_ENABLED_STATE_ENABLED
        } else {
            PackageManager.COMPONENT_ENABLED_STATE_DISABLED
        }

        if (packageManager.getComponentEnabledSetting(componente) == estado) {
            return
        }

        runCatching {
            packageManager.setComponentEnabledSetting(componente, estado, PackageManager.DONT_KILL_APP)
        }
    }

    private companion object {
        const val LANZADOR = "mx.schid.kiosko.LanzadorKiosko"
    }
}

private class FabricaViewModel(
    private val configuracion: ConfiguracionKiosko
) : ViewModelProvider.Factory {

    @Suppress("UNCHECKED_CAST")
    override fun <T : ViewModel> create(modelClass: Class<T>): T {
        return CapturaViewModel(configuracion, EnviadorHttp(configuracion)) as T
    }
}
