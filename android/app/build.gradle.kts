plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
}

// ---------------------------------------------------------------------------
// El certificado de la CA
//
// app/src/main/res/raw/schid_ca.crt NO está en el control de versiones: es un
// archivo que cada quien reemplaza con el certificado de su propia CA, y
// tenerlo versionado obligaba a volver a copiarlo en cada actualización del
// repositorio.
//
// Como el recurso se referencia desde network_security_config.xml, sin el
// archivo el proyecto ni siquiera enlaza. Por eso, si no existe, se copia el
// marcador de posición: un clon recién bajado compila sin pasos previos.
//
// Ver android/certificado/LEEME.md.
// ---------------------------------------------------------------------------
val certificadoCa = layout.projectDirectory.file("src/main/res/raw/schid_ca.crt").asFile
val certificadoMarcador = rootProject.layout.projectDirectory.file("certificado/schid_ca_marcador.crt").asFile

fun usaMarcador(): Boolean =
    certificadoCa.exists() && certificadoCa.readBytes().contentEquals(certificadoMarcador.readBytes())

val prepararCertificadoCa by tasks.registering {
    description = "Copia el marcador de posición de la CA si no hay un certificado real."
    outputs.file(certificadoCa)

    doLast {
        if (!certificadoCa.exists()) {
            certificadoCa.parentFile.mkdirs()
            certificadoMarcador.copyTo(certificadoCa)
        }
        if (usaMarcador()) {
            logger.warn(
                "SchId: se está compilando con el MARCADOR de la CA, no con un " +
                    "certificado real. La app no podrá conectar por https. " +
                    "Ver android/certificado/LEEME.md."
            )
        }
    }
}

/**
 * Un APK de release con el marcador dentro no puede hablar con ningún servidor.
 * Más vale enterarse aquí que en la ubicación, con la tableta ya montada.
 */
val exigirCertificadoReal by tasks.registering {
    description = "Falla si se intenta compilar un release con el marcador de la CA."
    dependsOn(prepararCertificadoCa)

    doLast {
        if (usaMarcador()) {
            throw GradleException(
                "No se puede compilar release con el marcador de posición de la CA.\n" +
                    "Copia tu schid_ca.crt sobre app/src/main/res/raw/schid_ca.crt.\n" +
                    "Ver android/certificado/LEEME.md."
            )
        }
    }
}

android {
    namespace = "mx.schid.kiosko"
    compileSdk = 35

    defaultConfig {
        applicationId = "mx.schid.kiosko"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "1.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    testOptions {
        unitTests {
            // Las clases de android.* son stubs que lanzan excepción en pruebas
            // unitarias. Sin esto, el Log.i del ViewModel aborta la corrutina de
            // envío y el flujo nunca llega a su estado final.
            isReturnDefaultValues = true
        }
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }
}

// El certificado tiene que estar en su lugar antes de que se empaqueten los
// recursos, y la exigencia del release antes de que se genere el APK.
tasks.named("preBuild") { dependsOn(prepararCertificadoCa) }
tasks.matching { it.name == "assembleRelease" || it.name == "bundleRelease" }
    .configureEach { dependsOn(exigirCertificadoReal) }

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.activity.compose)
    implementation(libs.kotlinx.coroutines.android)

    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.tooling.preview)
    debugImplementation(libs.androidx.compose.ui.tooling)
    implementation(libs.androidx.compose.material3)

    implementation(libs.androidx.camera.core)
    implementation(libs.androidx.camera.camera2)
    implementation(libs.androidx.camera.lifecycle)
    implementation(libs.androidx.camera.view)

    // Versión "bundled": el modelo viaja dentro del APK. Se eligió sobre la que
    // lo descarga por Play Services porque el kiosko vive en una red local
    // cerrada y puede no tener salida a internet.
    implementation(libs.mlkit.barcode.scanning)
    implementation(libs.mlkit.text.recognition)

    implementation(libs.okhttp)
    implementation(libs.androidx.security.crypto)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
}
