plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.cognitiveplatform.wear"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.cognitiveplatform.wear"
        minSdk = 30
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"

        val configuredApiBaseUrl = providers.gradleProperty("CP_WEAR_API_BASE_URL")
            .orElse("https://replace-with-your-cp-host/")
            .get()
            .let { if (it.endsWith('/')) it else "$it/" }
        buildConfigField("String", "CP_API_BASE_URL", "\"$configuredApiBaseUrl\"")
    }

    buildFeatures {
        buildConfig = true
        compose = true
    }

    composeOptions {
        kotlinCompilerExtensionVersion = "1.5.15"
    }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2025.06.01"))
    implementation("androidx.activity:activity-compose:1.10.1")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.wear.compose:compose-material3:1.6.2")
    implementation("androidx.wear.compose:compose-foundation:1.6.2")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")
    debugImplementation("androidx.compose.ui:ui-tooling")
}
