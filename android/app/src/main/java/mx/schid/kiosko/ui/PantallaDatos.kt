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
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.tooling.preview.Devices
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import mx.schid.kiosko.datos.ClavePasaporte
import mx.schid.kiosko.datos.Curp
import mx.schid.kiosko.datos.DocumentoCapturado
import mx.schid.kiosko.datos.OrigenDatos
import mx.schid.kiosko.datos.TipoDocumento
import java.util.Calendar

/**
 * Pantalla de datos. Cubre los dos casos finales del flujo, que son el mismo
 * formulario cambiando solo de qué parte:
 *
 * - **Confirmación**: llega con [inicial] lleno, con lo que se leyó del
 *   documento. El huésped revisa, corrige si hace falta y confirma. Nada se
 *   manda al servidor sin pasar por aquí.
 * - **Captura manual**: llega con [inicial] en nulo, porque ni el código ni el
 *   OCR pudieron con el documento. Alguien de recepción teclea los datos.
 *
 * Es la única pantalla del kiosko que muestra datos, y por eso vale la pena
 * decir qué datos son: **los que acaban de salir del documento que el huésped
 * tiene en la mano**, nunca el registro previo que hubiera en la base. Esa
 * distinción es la que se cuidó desde el principio — la API sigue sin
 * devolverle al kiosko nada del huésped.
 */
@Composable
fun PantallaDatos(
    tipoDocumento: TipoDocumento,
    inicial: DocumentoCapturado?,
    alConfirmar: (DocumentoCapturado) -> Unit,
    alCancelar: () -> Unit
) {
    val esConfirmacion = inicial != null
    val pasaporteInicial = inicial?.identidad?.let { ClavePasaporte.descomponer(it) }

    var curp by remember {
        mutableStateOf(if (tipoDocumento == TipoDocumento.INE) inicial?.identidad.orEmpty() else "")
    }
    var paisEmisor by remember { mutableStateOf(pasaporteInicial?.first.orEmpty()) }
    var numeroPasaporte by remember { mutableStateOf(pasaporteInicial?.second.orEmpty()) }
    var nombre by remember { mutableStateOf(inicial?.nombre.orEmpty()) }
    var direccion by remember { mutableStateOf(inicial?.direccion.orEmpty()) }
    var nacionalidad by remember {
        mutableStateOf(
            inicial?.nacionalidad
                ?: if (tipoDocumento == TipoDocumento.INE) "MEXICANA" else ""
        )
    }
    var error by remember { mutableStateOf<String?>(null) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(24.dp)
    ) {
        Text(
            if (esConfirmacion) "Revisa tus datos" else "Captura manual",
            style = MaterialTheme.typography.headlineSmall
        )

        Text(
            when {
                esConfirmacion -> "Si algo no coincide con tu documento, corrígelo antes de continuar."
                tipoDocumento == TipoDocumento.INE -> "No se pudo leer la credencial. Captura los datos."
                else -> "No se pudo leer el pasaporte. Captura los datos."
            },
            modifier = Modifier.padding(top = 8.dp)
        )

        // Aviso de que el dígito de control no cuadró. No impide continuar: si
        // el algoritmo tuviera un detalle mal, un documento legítimo detendría
        // el mostrador. Se pide revisar y ya.
        if (inicial?.identidadConsistente == false) {
            Text(
                "Algunos datos podrían haberse leído mal. Revísalos con cuidado.",
                color = Color(0xFFFFC107),
                modifier = Modifier.padding(top = 12.dp)
            )
        }

        if (tipoDocumento == TipoDocumento.INE) {
            OutlinedTextField(
                value = curp,
                onValueChange = { curp = it.uppercase() },
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
                when (
                    val resultado = construir(
                        tipoDocumento = tipoDocumento,
                        origen = inicial?.origen ?: OrigenDatos.MANUAL,
                        curp = curp,
                        paisEmisor = paisEmisor,
                        numeroPasaporte = numeroPasaporte,
                        nombre = nombre,
                        direccion = direccion,
                        nacionalidad = nacionalidad
                    )
                ) {
                    is ResultadoFormulario.Valido -> alConfirmar(resultado.documento)
                    is ResultadoFormulario.Invalido -> error = resultado.motivo
                }
            },
            modifier = Modifier.padding(top = 24.dp)
        ) {
            Text(if (esConfirmacion) "Confirmar y enviar" else "Registrar")
        }

        TextButton(onClick = alCancelar, modifier = Modifier.padding(top = 8.dp)) {
            Text("Cancelar")
        }
    }
}

private sealed interface ResultadoFormulario {
    data class Valido(val documento: DocumentoCapturado) : ResultadoFormulario
    data class Invalido(val motivo: String) : ResultadoFormulario
}

/**
 * Valida lo que quedó en el formulario, se haya tecleado o venga de la lectura
 * del documento. Un CURP mal escrito crea un huésped duplicado que nadie va a
 * notar, así que aquí sí se exige la estructura correcta — a diferencia del
 * dígito de control, que solo avisa.
 *
 * Se conserva el [origen] de la lectura original: si el huésped corrige un
 * campo de algo que se leyó por QR, sigue siendo interesante saber que vino de
 * un QR y no de una captura a mano.
 */
private fun construir(
    tipoDocumento: TipoDocumento,
    origen: OrigenDatos,
    curp: String,
    paisEmisor: String,
    numeroPasaporte: String,
    nombre: String,
    direccion: String,
    nacionalidad: String
): ResultadoFormulario {
    val ahora = Calendar.getInstance()

    if (nombre.isBlank()) {
        return ResultadoFormulario.Invalido("El nombre es obligatorio.")
    }

    return when (tipoDocumento) {
        TipoDocumento.INE -> {
            val limpio = Curp.normalizar(curp)
            if (!Curp.tieneEstructuraValida(limpio)) {
                ResultadoFormulario.Invalido("El CURP no tiene la estructura correcta. Revísalo.")
            } else {
                ResultadoFormulario.Valido(
                    DocumentoCapturado(
                        identidad = limpio,
                        tipoDocumento = TipoDocumento.INE,
                        origen = origen,
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
                pais.length != 3 ->
                    ResultadoFormulario.Invalido("El país emisor son 3 letras (ej. USA, CAN, ESP).")

                numero.isBlank() ->
                    ResultadoFormulario.Invalido("Falta el número de pasaporte.")

                else -> ResultadoFormulario.Valido(
                    DocumentoCapturado(
                        identidad = ClavePasaporte.generar(pais, numero),
                        tipoDocumento = TipoDocumento.PASAPORTE,
                        origen = origen,
                        nombre = nombre.trim().uppercase().ifBlank { null },
                        nacionalidad = nacionalidad.trim().uppercase().ifBlank { null }
                    )
                )
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Previsualizaciones: las cuatro combinaciones que puede tomar esta pantalla.
// ---------------------------------------------------------------------------

@Composable
private fun Envoltura(contenido: @Composable () -> Unit) {
    MaterialTheme(colorScheme = darkColorScheme()) {
        Surface(modifier = Modifier.fillMaxSize()) { contenido() }
    }
}

@Preview(name = "Confirmar · INE", showBackground = true, device = Devices.PIXEL_TABLET)
@Composable
private fun VistaPreviaConfirmarIne() {
    Envoltura {
        PantallaDatos(
            tipoDocumento = TipoDocumento.INE,
            inicial = DocumentoCapturado(
                identidad = "MELM850315HDFNPR07",
                tipoDocumento = TipoDocumento.INE,
                origen = OrigenDatos.QR,
                nombre = "JUAN PEREZ LOPEZ",
                direccion = "CALLE FALSA 123 COL CENTRO",
                nacionalidad = "MEXICANA",
                edad = 40
            ),
            alConfirmar = {},
            alCancelar = {}
        )
    }
}

@Preview(name = "Confirmar · Pasaporte", showBackground = true, device = Devices.PIXEL_TABLET)
@Composable
private fun VistaPreviaConfirmarPasaporte() {
    Envoltura {
        PantallaDatos(
            tipoDocumento = TipoDocumento.PASAPORTE,
            inicial = DocumentoCapturado(
                identidad = "PAS-UTO-L898902C3",
                tipoDocumento = TipoDocumento.PASAPORTE,
                origen = OrigenDatos.OCR,
                nombre = "ANNA MARIA ERIKSSON",
                nacionalidad = "UTO",
                edad = 50
            ),
            alConfirmar = {},
            alCancelar = {}
        )
    }
}

@Preview(name = "Manual · INE", showBackground = true, device = Devices.PIXEL_TABLET)
@Composable
private fun VistaPreviaManualIne() {
    Envoltura {
        PantallaDatos(TipoDocumento.INE, inicial = null, alConfirmar = {}, alCancelar = {})
    }
}

@Preview(name = "Manual · Pasaporte", showBackground = true, device = Devices.PIXEL_TABLET)
@Composable
private fun VistaPreviaManualPasaporte() {
    Envoltura {
        PantallaDatos(TipoDocumento.PASAPORTE, inicial = null, alConfirmar = {}, alCancelar = {})
    }
}
