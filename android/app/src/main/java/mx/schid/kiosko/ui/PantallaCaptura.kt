package mx.schid.kiosko.ui

import androidx.camera.view.PreviewView
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.RoundRect
import androidx.compose.ui.graphics.ClipOp
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.tooling.preview.Devices
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.compose.LocalLifecycleOwner
import mx.schid.kiosko.datos.RecorteCredencial
import mx.schid.kiosko.datos.TipoDocumento

/**
 * Pantalla única del kiosko.
 *
 * Salvo la pantalla de confirmación, lo que se ve nunca incluye información del
 * huésped: solo instrucciones y el desenlace. El kiosko está en un mostrador
 * donde cualquiera que pase alcanza a leer la pantalla.
 *
 * Y lo que la confirmación muestra son los datos que acaban de salir del
 * documento que el huésped tiene en la mano — nunca el registro previo que
 * hubiera en la base, que la API sigue sin devolverle al kiosko.
 */
@Composable
fun PantallaCaptura(
    viewModel: CapturaViewModel,
    alPedirConfiguracion: () -> Unit
) {
    val estado by viewModel.estado.collectAsState()
    val contexto = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current

    val camara = remember { CamaraKiosko(contexto, lifecycleOwner) }

    // El ViewModel no puede tocar la cámara, así que se le presta la función de
    // OCR. Es lo que le permite bajar al segundo escalón de la cadena de
    // lectura sin conocer nada de Android.
    DisposableEffect(camara) {
        viewModel.reconocerTexto = { jpeg, alTerminar -> camara.reconocerTexto(jpeg, alTerminar) }
        onDispose {
            viewModel.reconocerTexto = null
            camara.liberar()
        }
    }

    Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
        when (estado.paso) {
            Paso.INICIO -> Inicio(
                alComenzar = viewModel::comenzar,
                alPedirConfiguracion = alPedirConfiguracion
            )

            Paso.TIPO_DOCUMENTO -> ElegirTipo(viewModel::elegirTipo)

            Paso.FRENTE, Paso.REVERSO -> Camara(
                mensaje = estado.mensaje,
                avisoIdentidad = estado.avisoIdentidad,
                camara = camara,
                alDetectarCodigo = viewModel::codigoDetectado,
                alTomarFoto = { jpeg ->
                    if (jpeg == null) return@Camara
                    if (estado.paso == Paso.FRENTE) {
                        viewModel.frenteCapturado(jpeg)
                    } else {
                        viewModel.reversoCapturado(jpeg)
                    }
                }
            )

            Paso.LEYENDO, Paso.ENVIANDO -> Centrado {
                CircularProgressIndicator(modifier = Modifier.size(64.dp))
                Text(
                    estado.mensaje,
                    style = MaterialTheme.typography.headlineSmall,
                    color = Color.White,
                    modifier = Modifier.padding(top = 24.dp)
                )
            }

            Paso.NO_SE_PUDO_LEER -> Centrado {
                Text(
                    estado.mensaje,
                    style = MaterialTheme.typography.headlineSmall,
                    color = Color.White,
                    textAlign = TextAlign.Center
                )
                Button(
                    onClick = viewModel::reintentarFotos,
                    modifier = Modifier.padding(top = 32.dp)
                ) {
                    Text("Volver a tomar la foto")
                }
                TextButton(
                    onClick = viewModel::capturarAMano,
                    modifier = Modifier.padding(top = 8.dp)
                ) {
                    Text("Capturar los datos a mano", color = Color.LightGray)
                }
            }

            Paso.CONFIRMAR -> PantallaDatos(
                tipoDocumento = estado.tipoDocumento,
                inicial = estado.prellenado,
                alConfirmar = viewModel::confirmar,
                alCancelar = viewModel::cancelar
            )

            Paso.LISTO -> Mensaje(
                mensaje = estado.mensaje,
                destacado = true,
                textoBoton = "Terminar",
                alPulsar = viewModel::volverAlInicio
            )

            Paso.ERROR -> Mensaje(
                mensaje = estado.mensaje,
                destacado = false,
                textoBoton = if (estado.permiteReintentar) "Intentar de nuevo" else "Entendido",
                alPulsar = {
                    if (estado.permiteReintentar) viewModel.comenzar() else viewModel.volverAlInicio()
                }
            )
        }
    }
}

@Composable
private fun Inicio(alComenzar: () -> Unit, alPedirConfiguracion: () -> Unit) {
    Centrado {
        Text(
            "Registro de huéspedes",
            style = MaterialTheme.typography.headlineLarge,
            color = Color.White,
            textAlign = TextAlign.Center
        )
        Text(
            "Ten a la mano tu identificación",
            style = MaterialTheme.typography.bodyLarge,
            color = Color.White,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(top = 12.dp)
        )
        Button(onClick = alComenzar, modifier = Modifier.padding(top = 40.dp)) {
            Text("Comenzar")
        }

        // Acceso a ajustes. No se esconde: lo que protege la configuración es el
        // PIN que se pide al entrar, no que el botón sea difícil de encontrar.
        TextButton(
            onClick = alPedirConfiguracion,
            modifier = Modifier.padding(top = 64.dp)
        ) {
            Text("Ajustes", color = Color.DarkGray)
        }
    }
}

@Composable
private fun ElegirTipo(alElegir: (TipoDocumento) -> Unit) {
    Centrado {
        Text(
            "¿Con qué te vas a registrar?",
            style = MaterialTheme.typography.headlineMedium,
            color = Color.White,
            textAlign = TextAlign.Center
        )

        Button(
            onClick = { alElegir(TipoDocumento.INE) },
            modifier = Modifier.padding(top = 32.dp).fillMaxWidth()
        ) {
            Text("Credencial para votar (INE)")
        }

        Button(
            onClick = { alElegir(TipoDocumento.PASAPORTE) },
            modifier = Modifier.padding(top = 16.dp).fillMaxWidth()
        ) {
            Text("Pasaporte")
        }
    }
}

@Composable
private fun Camara(
    mensaje: String,
    avisoIdentidad: Boolean,
    camara: CamaraKiosko,
    alDetectarCodigo: (CodigoLeido) -> Unit,
    alTomarFoto: (ByteArray?) -> Unit
) {
    // El factory de AndroidView corre una sola vez, así que enganchar la cámara
    // ahí dejaba congelado lo que se capturara. Se hace en `update`, que corre
    // en cada recomposición: conectar es idempotente y barata después de la
    // primera vez.
    val detectar by rememberUpdatedState(alDetectarCodigo)

    Box(modifier = Modifier.fillMaxSize()) {
        // La caja tiene la proporción 3:4 de la captura, así que la vista previa
        // la llena exacta y sin franjas. Eso es lo que permite que la guía
        // dibujada encima corresponda punto por punto con lo que se recorta.
        Box(
            modifier = Modifier
                .align(Alignment.TopCenter)
                .fillMaxWidth()
                .aspectRatio(3f / 4f)
        ) {
            AndroidView(
                factory = { contexto -> PreviewView(contexto) },
                update = { vista -> camara.conectar(vista) { codigo -> detectar(codigo) } },
                onRelease = { camara.desconectar() },
                modifier = Modifier.fillMaxSize()
            )

            GuiaCredencial(modifier = Modifier.fillMaxSize())
        }

        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .background(Color(0xCC000000))
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(
                "Acomódalo dentro del recuadro: solo se guarda lo que quede dentro.",
                style = MaterialTheme.typography.bodyMedium,
                color = Color.LightGray,
                textAlign = TextAlign.Center,
                modifier = Modifier.padding(bottom = 8.dp)
            )

            Text(
                mensaje,
                style = MaterialTheme.typography.headlineSmall,
                color = Color.White,
                textAlign = TextAlign.Center
            )

            if (avisoIdentidad) {
                Text(
                    "Revisa los datos en recepción antes de continuar.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color(0xFFFFC107),
                    textAlign = TextAlign.Center,
                    modifier = Modifier.padding(top = 8.dp)
                )
            }

            Button(
                onClick = { camara.tomarFoto(alTomarFoto) },
                modifier = Modifier.padding(top = 16.dp)
            ) {
                Text("Tomar foto")
            }
        }
    }
}

/**
 * Pantalla de un solo mensaje con un botón. La usan tanto el desenlace bueno
 * como el error, que se diferencian nada más en el tamaño del texto y en lo que
 * dice el botón.
 */
@Composable
private fun Mensaje(
    mensaje: String,
    destacado: Boolean,
    textoBoton: String,
    alPulsar: () -> Unit
) {
    Centrado {
        Text(
            mensaje,
            style = if (destacado) {
                MaterialTheme.typography.headlineLarge
            } else {
                MaterialTheme.typography.headlineSmall
            },
            color = Color.White,
            textAlign = TextAlign.Center
        )
        Button(onClick = alPulsar, modifier = Modifier.padding(top = 32.dp)) {
            Text(textoBoton)
        }
    }
}

/**
 * Dibuja el rectángulo donde hay que acomodar el documento, y oscurece lo que
 * queda fuera para que se entienda de un vistazo que eso no se va a guardar.
 *
 * La geometría sale de [RecorteCredencial], el mismo cálculo que aplica el
 * recorte a la foto. No hay dos números que mantener de acuerdo.
 */
@Composable
private fun GuiaCredencial(modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val region = RecorteCredencial.calcular(size.width.toInt(), size.height.toInt())
        val esquina = size.width * 0.03f

        val hueco = Path().apply {
            addRoundRect(
                RoundRect(
                    left = region.x.toFloat(),
                    top = region.y.toFloat(),
                    right = (region.x + region.ancho).toFloat(),
                    bottom = (region.y + region.alto).toFloat(),
                    cornerRadius = CornerRadius(esquina, esquina)
                )
            )
        }

        // Todo lo de fuera se oscurece; el hueco queda limpio.
        clipPath(hueco, clipOp = ClipOp.Difference) {
            drawRect(color = Color(0xB3000000))
        }

        drawPath(
            path = hueco,
            color = Color.White,
            style = Stroke(width = 3.dp.toPx())
        )
    }
}

@Composable
private fun Centrado(contenido: @Composable () -> Unit) {
    Column(
        modifier = Modifier.fillMaxSize().padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        contenido()
    }
}

// ---------------------------------------------------------------------------
// Previsualizaciones
//
// Android Studio las dibuja al lado del código, sin compilar ni instalar nada
// en la tableta. Con el botón "Split" (arriba a la derecha del editor) se ven
// código y resultado en paralelo.
//
// Solo se previsualizan las pantallas que no dependen de la cámara: la vista
// previa de CameraX necesita hardware, así que ese paso hay que verlo en el
// dispositivo.
// ---------------------------------------------------------------------------

@Composable
private fun EnvolturaPrevia(contenido: @Composable () -> Unit) {
    MaterialTheme(colorScheme = darkColorScheme()) {
        Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
            contenido()
        }
    }
}

@Preview(name = "Inicio", showBackground = true, device = Devices.PIXEL_TABLET)
@Composable
private fun VistaPreviaInicio() {
    EnvolturaPrevia { Inicio(alComenzar = {}, alPedirConfiguracion = {}) }
}

@Preview(name = "Elegir documento", showBackground = true, device = Devices.PIXEL_TABLET)
@Composable
private fun VistaPreviaElegirTipo() {
    EnvolturaPrevia { ElegirTipo(alElegir = {}) }
}

@Preview(name = "Listo", showBackground = true, device = Devices.PIXEL_TABLET)
@Composable
private fun VistaPreviaListo() {
    EnvolturaPrevia {
        Mensaje(
            mensaje = "Listo. Puedes pasar a recepción.",
            destacado = true,
            textoBoton = "Terminar",
            alPulsar = {}
        )
    }
}

@Preview(name = "Error", showBackground = true, device = Devices.PIXEL_TABLET)
@Composable
private fun VistaPreviaError() {
    EnvolturaPrevia {
        Mensaje(
            mensaje = "No hay conexión con el servidor. Inténtalo de nuevo.",
            destacado = false,
            textoBoton = "Intentar de nuevo",
            alPulsar = {}
        )
    }
}
