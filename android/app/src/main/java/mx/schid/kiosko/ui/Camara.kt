package mx.schid.kiosko.ui

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.BitmapFactory
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageCapture
import androidx.camera.core.ImageCaptureException
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.core.content.ContextCompat
import androidx.lifecycle.LifecycleOwner
import com.google.mlkit.vision.barcode.BarcodeScanner
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions
import mx.schid.kiosko.datos.OrigenDatos
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

/** Un código leído, junto con de qué tipo era. */
data class CodigoLeido(
    val contenido: String,
    val origen: OrigenDatos,
    /** El formato tal como lo reporta ML Kit. Solo lo usa el modo diagnóstico. */
    val formatoCrudo: Int = Barcode.FORMAT_UNKNOWN
) {
    /** Nombre legible del formato, para poder identificar un código desconocido. */
    val nombreFormato: String
        get() = when (formatoCrudo) {
            Barcode.FORMAT_QR_CODE -> "QR"
            Barcode.FORMAT_PDF417 -> "PDF417"
            Barcode.FORMAT_AZTEC -> "Aztec"
            Barcode.FORMAT_DATA_MATRIX -> "DataMatrix"
            Barcode.FORMAT_CODE_128 -> "Code128"
            Barcode.FORMAT_CODE_39 -> "Code39"
            Barcode.FORMAT_CODE_93 -> "Code93"
            Barcode.FORMAT_CODABAR -> "Codabar"
            Barcode.FORMAT_EAN_13 -> "EAN-13"
            Barcode.FORMAT_EAN_8 -> "EAN-8"
            Barcode.FORMAT_ITF -> "ITF"
            Barcode.FORMAT_UPC_A -> "UPC-A"
            Barcode.FORMAT_UPC_E -> "UPC-E"
            else -> "desconocido ($formatoCrudo)"
        }
}

/**
 * Cámara del kiosko: vista previa, toma de foto, lectura de códigos y OCR.
 *
 * ## Por qué se vincula una sola vez
 *
 * El kiosko entra y sale de la pantalla de cámara varias veces por captura, y
 * muchas veces al día. La versión anterior volvía a vincular la cámara
 * (`unbindAll` y de nuevo `bindToLifecycle`) cada vez que la vista aparecía, y a
 * partir de la segunda vuelta dejaba de entregar códigos.
 *
 * Ahora los casos de uso se crean y vinculan **una sola vez**; lo único que pasa
 * al volver a entrar es que se le engancha la nueva superficie de dibujo. Menos
 * piezas que reconstruir es menos que se puede quedar a medias.
 *
 * ## Por qué el callback vive en un campo
 *
 * El analizador se crea al vincular, y si capturara la función de aviso se
 * quedaría con la primera para siempre. Guardarla en un campo que se reemplaza
 * en cada [conectar] hace imposible ese tipo de error.
 *
 * Las fotos NUNCA se escriben a disco: se capturan en memoria y lo que sale es
 * un arreglo de bytes que se manda y se borra.
 */
class CamaraKiosko(
    private val contexto: Context,
    private val lifecycleOwner: LifecycleOwner,
    /**
     * El modo diagnóstico busca TODOS los formatos, para poder identificar un
     * código que no sabemos qué es. En el flujo normal se limitan los formatos
     * para que el detector no gaste tiempo en los que no vienen en un documento
     * de identidad.
     */
    private val todosLosFormatos: Boolean = false
) {

    private var imageCapture: ImageCapture? = null
    private var preview: Preview? = null
    private var vinculada = false

    /** Lo lee el analizador en cada fotograma, así que nunca se queda viejo. */
    @Volatile
    private var alLeerCodigo: ((CodigoLeido) -> Unit)? = null

    private val ejecutor: ExecutorService = Executors.newSingleThreadExecutor()

    private val escaner: BarcodeScanner = if (todosLosFormatos) {
        BarcodeScanning.getClient()
    } else {
        BarcodeScanning.getClient(
            BarcodeScannerOptions.Builder()
                .setBarcodeFormats(Barcode.FORMAT_QR_CODE, Barcode.FORMAT_PDF417)
                .build()
        )
    }

    private val reconocedorTexto = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS)

    /**
     * Engancha la vista previa. Se puede llamar tantas veces como haga falta: la
     * primera vincula la cámara y las siguientes solo reemplazan la superficie.
     */
    fun conectar(vista: PreviewView, alLeerCodigo: (CodigoLeido) -> Unit) {
        this.alLeerCodigo = alLeerCodigo

        preview?.let {
            it.setSurfaceProvider(vista.surfaceProvider)
            return
        }

        if (vinculada) return
        vinculada = true

        val futuro = ProcessCameraProvider.getInstance(contexto)

        futuro.addListener({
            val proveedor = futuro.get()

            val nuevoPreview = Preview.Builder().build().also {
                it.setSurfaceProvider(vista.surfaceProvider)
            }

            val captura = ImageCapture.Builder()
                .setCaptureMode(ImageCapture.CAPTURE_MODE_MINIMIZE_LATENCY)
                .build()

            val analisis = ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
                .also { analizador ->
                    analizador.setAnalyzer(ejecutor, AnalizadorDeCodigos(escaner) { codigo ->
                        alLeerCodigo?.invoke(codigo)
                    })
                }

            proveedor.bindToLifecycle(
                lifecycleOwner,
                CameraSelector.DEFAULT_BACK_CAMERA,
                nuevoPreview,
                captura,
                analisis
            )

            preview = nuevoPreview
            imageCapture = captura
        }, ContextCompat.getMainExecutor(contexto))
    }

    /**
     * La vista previa se va de la pantalla. Se suelta la superficie —el
     * PreviewView que la daba está por destruirse— pero la cámara sigue
     * vinculada, lista para la próxima captura.
     */
    fun desconectar() {
        alLeerCodigo = null
        preview?.setSurfaceProvider(null)
    }

    /** Toma la foto en memoria y entrega el JPEG. */
    fun tomarFoto(alTerminar: (ByteArray?) -> Unit) {
        val captura = imageCapture ?: run {
            alTerminar(null)
            return
        }

        captura.takePicture(
            ContextCompat.getMainExecutor(contexto),
            object : ImageCapture.OnImageCapturedCallback() {
                override fun onCaptureSuccess(imagen: ImageProxy) {
                    alTerminar(imagen.use { aBytes(it) })
                }

                override fun onError(excepcion: ImageCaptureException) {
                    alTerminar(null)
                }
            }
        )
    }

    /**
     * Corre OCR sobre un JPEG que ya se capturó. Es el segundo escalón de la
     * cadena: entra cuando el código de barras no se pudo decodificar, sobre las
     * mismas fotos que ya se le tomaron al documento.
     */
    fun reconocerTexto(jpeg: ByteArray, alTerminar: (String?) -> Unit) {
        val bitmap = BitmapFactory.decodeByteArray(jpeg, 0, jpeg.size)
        if (bitmap == null) {
            alTerminar(null)
            return
        }

        reconocedorTexto.process(InputImage.fromBitmap(bitmap, 0))
            .addOnSuccessListener { texto -> alTerminar(texto.text) }
            .addOnFailureListener { alTerminar(null) }
            .addOnCompleteListener { bitmap.recycle() }
    }

    private fun aBytes(imagen: ImageProxy): ByteArray {
        val buffer = imagen.planes[0].buffer
        val bytes = ByteArray(buffer.remaining())
        buffer.get(bytes)
        return bytes
    }

    /** Se llama cuando la app deja de necesitar la cámara del todo. */
    fun liberar() {
        alLeerCodigo = null
        escaner.close()
        reconocedorTexto.close()
        ejecutor.shutdown()
    }
}

private class AnalizadorDeCodigos(
    private val escaner: BarcodeScanner,
    private val alLeer: (CodigoLeido) -> Unit
) : ImageAnalysis.Analyzer {

    @SuppressLint("UnsafeOptInUsageError")
    override fun analyze(imagen: ImageProxy) {
        val media = imagen.image
        if (media == null) {
            imagen.close()
            return
        }

        val entrada = InputImage.fromMediaImage(media, imagen.imageInfo.rotationDegrees)

        escaner.process(entrada)
            .addOnSuccessListener { codigos ->
                // El QR va primero: en los modelos de credencial que traen los
                // dos, es el más nuevo y el que se lee con menos reintentos.
                codigos
                    .sortedBy { if (it.format == Barcode.FORMAT_QR_CODE) 0 else 1 }
                    .forEach { codigo ->
                        val contenido = codigo.rawValue ?: return@forEach
                        val origen = if (codigo.format == Barcode.FORMAT_QR_CODE) {
                            OrigenDatos.QR
                        } else {
                            OrigenDatos.PDF417
                        }
                        alLeer(CodigoLeido(contenido, origen, codigo.format))
                    }
            }
            .addOnCompleteListener { imagen.close() }
    }
}
