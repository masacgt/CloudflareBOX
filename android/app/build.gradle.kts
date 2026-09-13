plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.cloudflarebox.app"
    compileSdk = 37
    defaultConfig {
        applicationId = "com.cloudflarebox.app"
        minSdk = 26
        targetSdk = 37
        versionCode = 4
        versionName = "0.1.3"
    }
    buildFeatures { compose = true }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2026.08.00"))
    implementation("androidx.activity:activity-compose:1.13.0")
    implementation("androidx.compose.material3:material3:1.4.0")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.11.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.11.0")
    implementation("androidx.savedstate:savedstate-ktx:1.5.0")
    implementation("androidx.work:work-runtime-ktx:2.10.1")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    implementation("org.jetbrains.kotlin:kotlin-stdlib-jdk7:2.2.10")
    implementation("org.jetbrains.kotlin:kotlin-stdlib-jdk8:2.2.10")
    debugImplementation("androidx.compose.ui:ui-tooling")
}
