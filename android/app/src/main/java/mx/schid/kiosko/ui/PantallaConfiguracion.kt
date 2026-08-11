package mx.schid.kiosko.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import mx.schid.kiosko.config.ConfiguracionKiosko
import mx.schid.kiosko.config.ValidadorUrl

/**
 * Ajustes del kiosko, detrás de un PIN. Es la única pantalla que pide algo
 * escrito, y por eso mismo no la ve un huésped: con la app anclada a la
 * pantalla, es también la única salida hacia la configuración del equipo.
 */
@Composable
fun PantallaConfiguracion(
    configuracion: ConfiguracionKiosko,
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

    Ajustes(configuracion = configuracion, alSalir = alSalir)
}

@Composable
private fun Ajustes(configuracion: ConfiguracionKiosko, alSalir: () -> Unit) {
    var url by remember { mutableStateOf(configuracion.urlBase) }
    var token by remember { mutableStateOf(configuracion.token) }
    var pinNuevo by remember { mutableStateOf("") }
    var mensaje by remember { mutableStateOf<String?>(null) }
    var error by remember { mutableStateOf<String?>(null) }

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
            label = { Text("Dirección del servidor") },
            placeholder = { Text("https://192.168.1.50:7443") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
        )

        // El puerto va en la propia URL. Se avisa de http porque es fácil
        // dejarlo puesto después de una prueba y no notarlo.
        if (ValidadorUrl.esSinCifrar(url)) {
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

        error?.let {
            Text(it, color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(top = 12.dp))
        }
        mensaje?.let {
            Text(it, modifier = Modifier.padding(top = 12.dp))
        }

        Button(
            onClick = {
                val problema = configuracion.validarUrl(url)
                when {
                    problema != null -> error = problema
                    token.isBlank() -> error = "Falta el token."
                    pinNuevo.isNotBlank() && pinNuevo.length < 4 ->
                        error = "El PIN debe tener al menos 4 dígitos."
                    else -> {
                        configuracion.urlBase = url
                        configuracion.token = token
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

        TextButton(onClick = alSalir, modifier = Modifier.padding(top = 8.dp)) {
            Text("Volver al kiosko")
        }
    }
}
