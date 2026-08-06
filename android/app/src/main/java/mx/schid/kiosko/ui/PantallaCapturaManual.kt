package mx.schid.kiosko.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
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
import androidx.compose.ui.unit.dp
import mx.schid.kiosko.datos.ClavePasaporte
import mx.schid.kiosko.datos.Curp
import mx.schid.kiosko.datos.DocumentoCapturado
import mx.schid.kiosko.datos.OrigenDatos
import mx.schid.kiosko.datos.TipoDocumento
import java.util.Calendar

/**
 * Último escalón de la cadena de lectura: la captura la hace una persona de
 * recepción cuando ni el código de barras ni el OCR pudieron con el documento.
 *
 * Esta pantalla sí muestra en pantalla lo que se está escribiendo, a diferencia
 * del resto del kiosko. No hay forma de evitarlo si alguien tiene que teclear
 * los datos; lo que sí se hace es no traer nada del registro previo y no dejar
 * nada al terminar.
 */
@Composable
fun PantallaCapturaManual(
    tipoDocumento: TipoDocumento,
    alConfirmar: (DocumentoCapturado) -> Unit,
    alCancelar: () -> Unit
) {
    var identidad by remember { mutableStateOf("") }
    var numeroPasaporte by remember { mutableStateOf("") }
    var paisEmisor by remember { mutableStateOf("") }
    var nombre by remember { mutableStateOf("") }
    var direccion by remember { mutableStateOf("") }
    var nacionalidad by remember { mutableStateOf(if (tipoDocumento == TipoDocumento.INE) "MEXICANA" else "") }
    var error by remember { mutableStateOf<String?>(null) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(24.dp)
    ) {
        Text("Captura manual", style = MaterialTheme.typography.headlineSmall)
        Text(
            when (tipoDocumento) {
                TipoDocumento.INE -> "No se pudo leer la credencial. Captura los datos."
                TipoDocumento.PASAPORTE -> "No se pudo leer el pasaporte. Captura los datos."
            },
            modifier = Modifier.padding(top = 8.dp)
        )

        if (tipoDocumento == TipoDocumento.INE) {
            OutlinedTextField(
                value = identidad,
                onValueChange = { identidad = it.uppercase() },
                label = { Text("CURP") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
            )
        } else {
            OutlinedTextField(
                value = paisEmisor,
                onValueChange = { paisEmisor = it.uppercase() },
                label = { Text("País emisor (3 letras, ej. USA)") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
            )
            OutlinedTextField(
                value = numeroPasaporte,
                onValueChange = { numeroPasaporte = it.uppercase() },
                label = { Text("Número de pasaporte") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
            )
        }

        OutlinedTextField(
            value = nombre,
            onValueChange = { nombre = it },
            label = { Text("Nombre completo") },
            modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
        )

        OutlinedTextField(
            value = nacionalidad,
            onValueChange = { nacionalidad = it },
            label = { Text("Nacionalidad") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
        )

        if (tipoDocumento == TipoDocumento.INE) {
            OutlinedTextField(
                value = direccion,
                onValueChange = { direccion = it },
                label = { Text("Domicilio") },
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp)
            )
        }

        error?.let {
            Text(
                it,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(top = 12.dp)
            )
        }

        Button(
            onClick = {
                val documento = construir(
                    tipoDocumento = tipoDocumento,
                    curp = identidad,
                    paisEmisor = paisEmisor,
                    numeroPasaporte = numeroPasaporte,
                    nombre = nombre,
                    direccion = direccion,
                    nacionalidad = nacionalidad
                )

                when (documento) {
                    is ResultadoManual.Valido -> alConfirmar(documento.documento)
                    is ResultadoManual.Invalido -> error = documento.motivo
                }
            },
            modifier = Modifier.padding(top = 24.dp)
        ) {
            Text("Registrar")
        }

        TextButton(onClick = alCancelar, modifier = Modifier.padding(top = 8.dp)) {
            Text("Cancelar")
        }
    }
}

private sealed interface ResultadoManual {
    data class Valido(val documento: DocumentoCapturado) : ResultadoManual
    data class Invalido(val motivo: String) : ResultadoManual
}

/**
 * Valida lo capturado a mano. Un CURP mal tecleado crea un huésped duplicado
 * que nadie va a notar, así que aquí sí se exige que tenga la estructura
 * correcta — a diferencia del dígito de control, que solo avisa.
 */
private fun construir(
    tipoDocumento: TipoDocumento,
    curp: String,
    paisEmisor: String,
    numeroPasaporte: String,
    nombre: String,
    direccion: String,
    nacionalidad: String
): ResultadoManual {
    val ahora = Calendar.getInstance()

    if (nombre.isBlank()) {
        return ResultadoManual.Invalido("El nombre es obligatorio en una captura manual.")
    }

    return when (tipoDocumento) {
        TipoDocumento.INE -> {
            val limpio = Curp.normalizar(curp)
            if (!Curp.tieneEstructuraValida(limpio)) {
                ResultadoManual.Invalido("El CURP no tiene la estructura correcta. Revísalo.")
            } else {
                ResultadoManual.Valido(
                    DocumentoCapturado(
                        identidad = limpio,
                        tipoDocumento = TipoDocumento.INE,
                        origen = OrigenDatos.MANUAL,
                        nombre = nombre.trim().uppercase().ifBlank { null },
                        direccion = direccion.trim().uppercase().ifBlank { null },
                        nacionalidad = nacionalidad.trim().uppercase().ifBlank { null },
                        edad = Curp.edad(
                            limpio,
                            ahora.get(Calendar.YEAR),
                            ahora.get(Calendar.MONTH) + 1,
                            ahora.get(Calendar.DAY_OF_MONTH)
                        ),
                        identidadConsistente = Curp.digitoVerificadorCoincide(limpio)
                    )
                )
            }
        }

        TipoDocumento.PASAPORTE -> {
            val pais = paisEmisor.trim()
            val numero = numeroPasaporte.trim()
            when {
                pais.length != 3 -> ResultadoManual.Invalido("El país emisor son 3 letras (ej. USA, CAN, ESP).")
                numero.isBlank() -> ResultadoManual.Invalido("Falta el número de pasaporte.")
                else -> ResultadoManual.Valido(
                    DocumentoCapturado(
                        identidad = ClavePasaporte.generar(pais, numero),
                        tipoDocumento = TipoDocumento.PASAPORTE,
                        origen = OrigenDatos.MANUAL,
                        nombre = nombre.trim().uppercase().ifBlank { null },
                        nacionalidad = nacionalidad.trim().uppercase().ifBlank { null }
                    )
                )
            }
        }
    }
}
