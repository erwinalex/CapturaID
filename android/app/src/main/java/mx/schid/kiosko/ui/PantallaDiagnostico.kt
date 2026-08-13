package mx.schid.kiosko.ui

import androidx.camera.view.PreviewView
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.compose.LocalLifecycleOwner
import mx.schid.kiosko.R
import mx.schid.kiosko.config.CaIncrustada
import mx.schid.kiosko.datos.Curp
import mx.schid.kiosko.datos.Mrz

/**
 * Modo diagnóstico: enseña **tal cual** lo que la cámara logra sacar de un
 * documento, sin interpretarlo.
 *
 * Existe porque el contenido del QR y del PDF417 de la INE no está documentado
 * públicamente y cambia entre modelos de credencial. Sin ver el contenido real
 * de una credencial concreta no hay forma de saber si el CURP viene como texto,
 * en qué posición, ni con qué separadores — y por lo tanto no se puede escribir
 * un mapeo correcto de nombre y domicilio.
 *
 * Va detrás del PIN de administración y muestra datos personales en pantalla,
 * así que **es para configurar con un documento de prueba, no para el
 * mostrador**. Nada de lo que aparece aquí se guarda ni se manda a ningún lado.
 */
@Composable
fun PantallaDiagnostico(alSalir: () -> Unit) {
    val contexto = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val portapapeles = LocalClipboardManager.current

    // La CA no cambia mientras la app viva: se lee una vez.
    val ca = remember {
        contexto.resources.openRawResource(R.raw.schid_ca).use { CaIncrustada.leer(it) }
    }

    // Con todos los formatos: en el reverso de la INE hay códigos grandes que no
    // son ni QR ni PDF417, y sin esto el escáner ni los mira.
    val camara = remember { CamaraKiosko(contexto, lifecycleOwner, todosLosFormatos = true) }

    // Se acumulan por formato: el reverso trae varios códigos y hay que poder
    // verlos todos, no solo el último que pasó por la cámara.
    val codigos = remember { mutableStateMapOf<String, CodigoLeido>() }
    var textoOcr by remember { mutableStateOf<String?>(null) }
    var analizando by remember { mutableStateOf(false) }

    val alDetectar by rememberUpdatedState<(CodigoLeido) -> Unit> { leido ->
        codigos[leido.nombreFormato + "/" + leido.contenido.length] = leido
    }

    DisposableEffect(camara) {
        onDispose { camara.liberar() }
    }

    Column(modifier = Modifier.fillMaxSize().padding(16.dp)) {
        Text("Diagnóstico de lectura", style = MaterialTheme.typography.headlineSmall)
        Text(
            "Usa un documento de prueba: esta pantalla muestra su contenido en claro.",
            color = Color(0xFFB26A00),
            modifier = Modifier.padding(top = 4.dp)
        )

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(220.dp)
                .padding(top = 12.dp)
                .background(Color.Black)
        ) {
            AndroidView(
                factory = { ctx -> PreviewView(ctx) },
                update = { vista -> camara.conectar(vista) { leido -> alDetectar(leido) } },
                onRelease = { camara.desconectar() },
                modifier = Modifier.fillMaxSize()
            )
        }

        Button(
            onClick = {
                analizando = true
                camara.tomarFoto { jpeg ->
                    if (jpeg == null) {
                        textoOcr = "(no se pudo tomar la foto)"
                        analizando = false
                    } else {
                        camara.reconocerTexto(jpeg) { texto ->
                            textoOcr = texto ?: "(el OCR no devolvió nada)"
                            analizando = false
                        }
                    }
                }
            },
            enabled = !analizando,
            modifier = Modifier.padding(top = 12.dp)
        ) {
            Text(if (analizando) "Analizando..." else "Analizar con OCR")
        }

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp)
                .verticalScroll(rememberScrollState())
        ) {
            Seccion("CA compilada en la app") {
                if (ca == null) {
                    Text(
                        "No se pudo leer el certificado de la CA del APK. " +
                            "El archivo res/raw/schid_ca.crt no es un certificado válido."
                    )
                } else {
                    if (ca.esMarcador) {
                        Text(
                            "Esta app se compiló con el MARCADOR de posición, no con una CA " +
                                "real. No va a poder conectar con ningún servidor. Copia tu " +
                                "schid_ca.crt sobre app/src/main/res/raw/ y recompila.",
                            color = Color(0xFFFF6B6B)
                        )
                    }
                    if (ca.vencido) {
                        Text("La CA compilada está VENCIDA.", color = Color(0xFFFF6B6B))
                    }
                    Text("Sujeto: ${ca.sujeto}")
                    Text("Vigencia: ${ca.valeDesde} a ${ca.valeHasta}")
                    Text("Huella SHA-1 (el 'Thumbprint' de Windows):")
                    Crudo(ca.huellaSha1)
                    Text(
                        "Tiene que ser idéntica a la de la CA del servidor. En PowerShell: " +
                            "(Get-PfxCertificate C:\\SchId\\certificados\\schid_ca.crt).Thumbprint"
                    )
                }
            }

            Seccion("Códigos de barras (${codigos.size})") {
                if (codigos.isEmpty()) {
                    Text("Todavía no se ha leído ningún código. Mueve el documento despacio.")
                } else {
                    codigos.values.forEach { leido ->
                        Text("Formato: ${leido.nombreFormato}")
                        Text("Largo: ${leido.contenido.length} caracteres")
                        Text("¿Trae un CURP reconocible?: ${Curp.buscarEn(leido.contenido) ?: "no"}")
                        Crudo(leido.contenido)
                    }
                }
            }

            Seccion("Texto reconocido (OCR)") {
                val texto = textoOcr
                if (texto == null) {
                    Text("Toma una foto con el botón de arriba para ver qué texto reconoce.")
                } else {
                    Text("Largo: ${texto.length} caracteres")
                    Text("¿Trae un CURP reconocible?: ${Curp.buscarEn(texto) ?: "no"}")
                    Text("¿Trae una MRZ de pasaporte?: ${if (Mrz.buscarEn(texto) != null) "sí" else "no"}")
                    Crudo(texto)
                }
            }
        }

        Button(
            onClick = {
                val informe = buildString {
                    appendLine("=== CA COMPILADA EN LA APP ===")
                    if (ca == null) {
                        appendLine("(no se pudo leer)")
                    } else {
                        appendLine("Sujeto: ${ca.sujeto}")
                        appendLine("Vigencia: ${ca.valeDesde} a ${ca.valeHasta}")
                        appendLine("SHA-1:   ${ca.huellaSha1}")
                        appendLine("SHA-256: ${ca.huellaSha256}")
                        if (ca.esMarcador) appendLine("¡ES EL MARCADOR DE POSICIÓN!")
                        if (ca.vencido) appendLine("¡VENCIDA!")
                    }
                    appendLine()
                    appendLine("=== CÓDIGOS DE BARRAS (${codigos.size}) ===")
                    if (codigos.isEmpty()) {
                        appendLine("(ninguno)")
                    } else {
                        codigos.values.forEach {
                            appendLine("--- ${it.nombreFormato}, ${it.contenido.length} caracteres")
                            appendLine(it.contenido)
                        }
                    }
                    appendLine()
                    appendLine("=== OCR ===")
                    appendLine(textoOcr ?: "(ninguno)")
                }
                portapapeles.setText(AnnotatedString(informe))
            },
            modifier = Modifier.padding(top = 12.dp)
        ) {
            Text("Copiar todo al portapapeles")
        }

        TextButton(onClick = alSalir, modifier = Modifier.padding(top = 4.dp)) {
            Text("Volver")
        }
    }
}

@Composable
private fun Seccion(titulo: String, contenido: @Composable () -> Unit) {
    Column(modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp)) {
        Text(titulo, style = MaterialTheme.typography.titleMedium)
        contenido()
    }
}

/**
 * El contenido crudo, en monoespaciado y sin ajuste de línea: la posición de
 * cada carácter importa para poder mapear los campos, y un salto de línea
 * automático la haría irreconocible.
 */
@Composable
private fun Crudo(texto: String) {
    Text(
        texto,
        fontFamily = FontFamily.Monospace,
        fontSize = 11.sp,
        softWrap = false,
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 8.dp)
            .background(Color(0xFF1B1B1B))
            .padding(8.dp)
            .horizontalScroll(rememberScrollState())
    )
}
