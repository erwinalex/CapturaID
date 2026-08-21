package mx.schid.kiosko.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import mx.schid.kiosko.config.ConfiguracionKiosko
import mx.schid.kiosko.config.DireccionServidor
import mx.schid.kiosko.red.ResultadoPrueba
import mx.schid.kiosko.red.SchIdApi
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Ajustes del kiosko, detrás de un PIN. Es la única pantalla que pide algo
 * escrito, y por eso mismo no la ve un huésped: con la app anclada a la
 * pantalla, es también la única salida hacia la configuración del equipo.
 */
@Composable
fun PantallaConfiguracion(
    configuracion: ConfiguracionKiosko,
    alAbrirDiagnostico: () -> Unit,
    alSalir: () -> Unit
) {
    var pinCapturado by remember { mutableStateOf("") }
    var desbloqueada by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }

    if (!desbloqueada) {
        Column(
            modifier = Modifier.fillMaxSize().padding(32.dp)
        ) {
            Text("PIN de administración", style = MaterialTheme.typography.headlineSmall)

            OutlinedTextField(
                value = pinCapturado,
                onValueChange = { pinCapturado = it },
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
            )

            error?.let {
                Text(it, color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(top = 8.dp))
            }

            Button(
                onClick = {
                    if (pinCapturado == configuracion.pinAdministracion) {
                        desbloqueada = true
                        error = null
                    } else {
                        error = "PIN incorrecto."
                        pinCapturado = ""
                    }
                },
                modifier = Modifier.padding(top = 16.dp)
            ) {
                Text("Entrar")
            }

            TextButton(onClick = alSalir, modifier = Modifier.padding(top = 8.dp)) {
                Text("Volver")
            }
        }
        return
    }

    Ajustes(
        configuracion = configuracion,
        alAbrirDiagnostico = alAbrirDiagnostico,
        alSalir = alSalir
    )
}

@Composable
private fun Ajustes(
    configuracion: ConfiguracionKiosko,
    alAbrirDiagnostico: () -> Unit,
    alSalir: () -> Unit
) {
    var url by remember { mutableStateOf(configuracion.direccionServidor) }
    var token by remember { mutableStateOf(configuracion.token) }
    var pinNuevo by remember { mutableStateOf("") }
    var mensaje by remember { mutableStateOf<String?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    var probando by remember { mutableStateOf(false) }
    var modoKiosko by remember { mutableStateOf(configuracion.modoKiosko) }
    var resultadoPrueba by remember { mutableStateOf<ResultadoPrueba?>(null) }
    val alcance = rememberCoroutineScope()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(32.dp)
    ) {
        Text("Configuración del kiosko", style = MaterialTheme.typography.headlineSmall)

        if (configuracion.usaPinDeFabrica) {
            Text(
                "Este kiosko sigue con el PIN de fábrica. Cámbialo antes de dejarlo en el mostrador.",
                color = Color(0xFFB26A00),
                modifier = Modifier.padding(top = 12.dp)
            )
        }

        OutlinedTextField(
            value = url,
            onValueChange = { url = it },
            label = { Text("IP del servidor") },
            placeholder = { Text("192.168.1.226:7443") },
            supportingText = {
                Text("Solo la IP y el puerto. El nombre del certificado lo pone la app.")
            },
            singleLine = true,
            modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
        )

        // El puerto va en la propia URL. Se avisa de http porque es fácil
        // dejarlo puesto después de una prueba y no notarlo.
        if (DireccionServidor.esSinCifrar(url)) {
            Text(
                "Sin cifrar: el token viaja en claro por la red. Úsalo solo para pruebas.",
                color = Color(0xFFB26A00),
                modifier = Modifier.padding(top = 8.dp)
            )
        }

        OutlinedTextField(
            value = token,
            onValueChange = { token = it },
            label = { Text("Token del kiosko") },
            visualTransformation = PasswordVisualTransformation(),
            singleLine = true,
            modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
        )

        OutlinedTextField(
            value = pinNuevo,
            onValueChange = { pinNuevo = it },
            label = { Text("Nuevo PIN (dejar vacío para no cambiarlo)") },
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
            singleLine = true,
            modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
        )

        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier.fillMaxWidth().padding(top = 24.dp)
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text("Modo kiosko", style = MaterialTheme.typography.titleMedium)
                Text(
                    if (modoKiosko) {
                        "La app se ancla a la pantalla y el dispositivo no se apaga solo. " +
                            "El huésped no puede salirse a otras aplicaciones."
                    } else {
                        "El dispositivo se puede usar para otras cosas. Los datos de la INE " +
                            "siguen protegidos contra capturas de pantalla."
                    },
                    style = MaterialTheme.typography.bodySmall
                )
            }
            Switch(
                checked = modoKiosko,
                onCheckedChange = { modoKiosko = it },
                modifier = Modifier.padding(start = 12.dp)
            )
        }

        error?.let {
            Text(it, color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(top = 12.dp))
        }
        mensaje?.let {
            Text(it, modifier = Modifier.padding(top = 12.dp))
        }

        Button(
            onClick = {
                val problema = configuracion.validarDireccion(url)
                when {
                    problema != null -> error = problema
                    token.isBlank() -> error = "Falta el token."
                    pinNuevo.isNotBlank() && pinNuevo.length < 4 ->
                        error = "El PIN debe tener al menos 4 dígitos."
                    else -> {
                        configuracion.direccionServidor = url
                        configuracion.token = token
                        configuracion.modoKiosko = modoKiosko
                        if (pinNuevo.isNotBlank()) {
                            configuracion.pinAdministracion = pinNuevo
                            pinNuevo = ""
                        }
                        error = null
                        mensaje = "Guardado."
                    }
                }
            },
            modifier = Modifier.padding(top = 24.dp)
        ) {
            Text("Guardar")
        }

        // Prueba la conexión de verdad, con la URL y el token capturados. Sin
        // esto, el único momento de enterarse de un problema de certificado o
        // de firewall era con un huésped enfrente — y el mensaje que ve el
        // huésped, a propósito, no dice nada técnico.
        Button(
            onClick = {
                val problema = configuracion.validarDireccion(url)
                if (problema != null) {
                    resultadoPrueba = ResultadoPrueba(false, problema)
                    return@Button
                }

                probando = true
                resultadoPrueba = null
                alcance.launch {
                    val resultado = withContext(Dispatchers.IO) {
                        val destino = DireccionServidor.interpretar(url)
                        if (destino == null) {
                            ResultadoPrueba(false, "No se entiende la dirección.")
                        } else {
                            SchIdApi(destino, token.trim()).probarConexion()
                        }
                    }
                    resultadoPrueba = resultado
                    probando = false
                }
            },
            enabled = !probando && token.isNotBlank(),
            modifier = Modifier.padding(top = 24.dp)
        ) {
            Text(if (probando) "Probando..." else "Probar conexión")
        }

        resultadoPrueba?.let { resultado ->
            Text(
                resultado.mensaje,
                color = if (resultado.correcto) Color(0xFF2E7D32) else MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(top = 12.dp)
            )
        }

        TextButton(onClick = alAbrirDiagnostico, modifier = Modifier.padding(top = 16.dp)) {
            Text("Diagnóstico de lectura")
        }

        TextButton(onClick = alSalir) {
            Text("Volver al kiosko")
        }
    }
}
