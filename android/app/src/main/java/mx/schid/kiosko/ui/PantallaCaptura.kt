package mx.schid.kiosko.ui

import androidx.camera.view.PreviewView
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.compose.LocalLifecycleOwner
import mx.schid.kiosko.datos.TipoDocumento

/**
 * Pantalla única del kiosko.
 *
 * Salvo la captura manual —donde alguien tiene que teclear los datos— lo que se
 * ve nunca incluye información del huésped: solo instrucciones y el desenlace.
 * Es una decisión, no un pendiente: el kiosko está en un mostrador donde
 * cualquiera que pase alcanza a leer la pantalla.
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
                buscarCodigo = estado.paso == Paso.REVERSO,
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

            Paso.MANUAL -> PantallaCapturaManual(
                tipoDocumento = estado.tipoDocumento,
                alConfirmar = viewModel::capturaManual,
                alCancelar = viewModel::cancelarCapturaManual
            )

            Paso.LISTO -> Centrado {
                Text(
                    estado.mensaje,
                    style = MaterialTheme.typography.headlineLarge,
                    color = Color.White,
                    textAlign = TextAlign.Center
                )
                Button(
                    onClick = viewModel::volverAlInicio,
                    modifier = Modifier.padding(top = 32.dp)
                ) {
                    Text("Terminar")
                }
            }

            Paso.ERROR -> Centrado {
                Text(
                    estado.mensaje,
                    style = MaterialTheme.typography.headlineSmall,
                    color = Color.White,
                    textAlign = TextAlign.Center
                )
                Button(
                    onClick = {
                        if (estado.permiteReintentar) viewModel.comenzar() else viewModel.volverAlInicio()
                    },
                    modifier = Modifier.padding(top = 32.dp)
                ) {
                    Text(if (estado.permiteReintentar) "Intentar de nuevo" else "Entendido")
                }
            }
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
    buscarCodigo: Boolean,
    alDetectarCodigo: (CodigoLeido) -> Unit,
    alTomarFoto: (ByteArray?) -> Unit
) {
    Box(modifier = Modifier.fillMaxSize()) {
        AndroidView(
            factory = { contexto ->
                PreviewView(contexto).also { vista ->
                    camara.iniciar(vista) { codigo ->
                        if (buscarCodigo) alDetectarCodigo(codigo)
                    }
                }
            },
            modifier = Modifier.fillMaxSize()
        )

        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .background(Color(0xCC000000))
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
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
