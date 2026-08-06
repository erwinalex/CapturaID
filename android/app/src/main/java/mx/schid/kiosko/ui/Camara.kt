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
import java.io.ByteArrayOutputStream
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

/**
 * Cámara del kiosko: vista previa, toma de foto y lectura del código de barras.
 *
 * Las fotos NUNCA se escriben a disco. `ImageCapture.takePicture` se usa en su
 * variante en memoria y lo que sale es un arreglo de bytes que se manda y se
 * borra. Guardarlas aunque fuera temporalmente dejaría imágenes de INE en el
 * almacenamiento de una tableta que está en un mostrador.
 */
class CamaraKiosko(
    private val contexto: Context,
    private val lifecycleOwner: LifecycleOwner
) {

    private var imageCapture: ImageCapture? = null
    private val ejecutor: ExecutorService = Executors.newSingleThreadExecutor()

    private val escaner: BarcodeScanner = BarcodeScanning.getClient(
        BarcodeScannerOptions.Builder()
            // El reverso de la INE trae PDF417; los modelos nuevos además un QR.
            // Se limitan los formatos para que el detector no gaste tiempo
            // buscando los otros doce.
            .setBarcodeFormats(Barcode.FORMAT_PDF417, Barcode.FORMAT_QR_CODE)
            .build()
    )

    fun iniciar(
        vista: PreviewView,
        alDetectarCodigo: (String) -> Unit
    ) {
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
                .also { it.setAnalyzer(ejecutor, AnalizadorDeCodigos(escaner, alDetectarCodigo)) }

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
                    val jpeg = imagen.use { aBytes(it) }
                    alTerminar(jpeg)
                }

                override fun onError(excepcion: ImageCaptureException) {
                    alTerminar(null)
                }
            }
        )
    }

    private fun aBytes(imagen: ImageProxy): ByteArray {
        val buffer = imagen.planes[0].buffer
        val salida = ByteArrayOutputStream(buffer.remaining())
        val bytes = ByteArray(buffer.remaining())
        buffer.get(bytes)
        salida.write(bytes)
        return salida.toByteArray()
    }

    fun liberar() {
        escaner.close()
        ejecutor.shutdown()
    }
}

private class AnalizadorDeCodigos(
    private val escaner: BarcodeScanner,
    private val alDetectar: (String) -> Unit
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
                codigos.forEach { codigo ->
                    codigo.rawValue?.let(alDetectar)
                }
            }
            .addOnCompleteListener { imagen.close() }
    }
}
