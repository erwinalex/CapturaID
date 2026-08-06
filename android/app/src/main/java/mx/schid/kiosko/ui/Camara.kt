package mx.schid.kiosko.ui

import android.annotation.SuppressLint
import android.content.Context
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
data class CodigoLeido(val contenido: String, val origen: OrigenDatos)

/**
 * Cámara del kiosko: vista previa, toma de foto, lectura de códigos y OCR.
 *
 * Las fotos NUNCA se escriben a disco. `ImageCapture.takePicture` se usa en su
 * variante en memoria y lo que sale es un arreglo de bytes que se manda y se
 * borra. Guardarlas aunque fuera temporalmente dejaría imágenes de documentos de
 * identidad en el almacenamiento de una tableta que está en un mostrador.
 */
class CamaraKiosko(
    private val contexto: Context,
    private val lifecycleOwner: LifecycleOwner
) {

    private var imageCapture: ImageCapture? = null
    private val ejecutor: ExecutorService = Executors.newSingleThreadExecutor()

    private val escaner: BarcodeScanner = BarcodeScanning.getClient(
        BarcodeScannerOptions.Builder()
            // El reverso de la INE trae PDF417, y los modelos nuevos además un
            // QR. Se limitan los formatos para que el detector no gaste tiempo
            // buscando los otros doce.
            .setBarcodeFormats(Barcode.FORMAT_QR_CODE, Barcode.FORMAT_PDF417)
            .build()
    )

    private val reconocedorTexto = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS)

    fun iniciar(vista: PreviewView, alLeerCodigo: (CodigoLeido) -> Unit) {
        val futuro = ProcessCameraProvider.getInstance(contexto)

        futuro.addListener({
            val proveedor = futuro.get()

            val preview = Preview.Builder().build().also {
                it.setSurfaceProvider(vista.surfaceProvider)
            }

            val captura = ImageCapture.Builder()
                .setCaptureMode(ImageCapture.CAPTURE_MODE_MINIMIZE_LATENCY)
                .build()

            val analisis = ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
                .also { it.setAnalyzer(ejecutor, AnalizadorDeCodigos(escaner, alLeerCodigo)) }

            proveedor.unbindAll()
            proveedor.bindToLifecycle(
                lifecycleOwner,
                CameraSelector.DEFAULT_BACK_CAMERA,
                preview,
                captura,
                analisis
            )

            imageCapture = captura
        }, ContextCompat.getMainExecutor(contexto))
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
     * cadena: se usa cuando el código de barras no se pudo decodificar, sobre
     * las mismas fotos que ya se le tomaron al documento — no se le pide nada
     * más al huésped.
     */
    fun reconocerTexto(jpeg: ByteArray, alTerminar: (String?) -> Unit) {
        val bitmap = android.graphics.BitmapFactory.decodeByteArray(jpeg, 0, jpeg.size)
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

    fun liberar() {
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
                val ordenados = codigos.sortedBy { if (it.format == Barcode.FORMAT_QR_CODE) 0 else 1 }

                ordenados.forEach { codigo ->
                    val contenido = codigo.rawValue ?: return@forEach
                    val origen = if (codigo.format == Barcode.FORMAT_QR_CODE) {
                        OrigenDatos.QR
                    } else {
                        OrigenDatos.PDF417
                    }
                    alLeer(CodigoLeido(contenido, origen))
                }
            }
            .addOnCompleteListener { imagen.close() }
    }
}
