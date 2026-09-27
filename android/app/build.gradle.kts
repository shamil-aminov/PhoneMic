import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

/**
 * Release signing.
 *
 * Credentials never live in the repository. They come from either
 * `keystore.properties` next to `settings.gradle.kts` (local builds,
 * git-ignored) or from environment variables (CI, via repository secrets).
 * With neither, a release build is signed with the debug key: installable for
 * trying things out, but the release workflow refuses to publish it.
 *
 * See docs/RELEASING.md.
 */
val keystorePropertiesFile = rootProject.file("keystore.properties")
val keystoreProperties = Properties().apply {
    if (keystorePropertiesFile.exists()) {
        keystorePropertiesFile.inputStream().use { load(it) }
    }
}

fun secret(key: String, env: String): String? =
    keystoreProperties.getProperty(key) ?: System.getenv(env)

val releaseStorePath = secret("storeFile", "PHONEMIC_STORE_FILE")
val hasReleaseSigning = releaseStorePath != null && file(releaseStorePath).exists()

android {
    namespace = "sh.aminov.phonemic"
    compileSdk {
        version = release(37)
    }

    defaultConfig {
        applicationId = "sh.aminov.phonemic"
        minSdk = 26
        targetSdk = 37
        versionCode = 1
        versionName = "1.0.0"
    }

    signingConfigs {
        if (hasReleaseSigning) {
            create("release") {
                storeFile = file(releaseStorePath!!)
                storePassword = secret("storePassword", "PHONEMIC_STORE_PASSWORD")
                keyAlias = secret("keyAlias", "PHONEMIC_KEY_ALIAS")
                keyPassword = secret("keyPassword", "PHONEMIC_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            signingConfig = signingConfigs.getByName(if (hasReleaseSigning) "release" else "debug")
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    buildFeatures {
        compose = true
    }
}

dependencies {
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.service)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.core.ktx)
    implementation(libs.play.services.code.scanner)
    // The scanner drags in an old Fragment that breaks ActivityResult permission requests.
    implementation(libs.androidx.fragment)
    testImplementation(libs.junit)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
