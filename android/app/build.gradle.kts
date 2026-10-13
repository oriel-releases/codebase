import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

/**
 * The Unsplash access key is baked in at build time, like the desktop apps: the UNSPLASH_ACCESS_KEY environment
 * variable, else `android/.env`, else the Windows app's `windows/.env` (all git-ignored).
 */
fun unsplashKey(): String {
    System.getenv("UNSPLASH_ACCESS_KEY")?.takeIf { it.isNotBlank() }?.let { return it }
    for (path in listOf("../.env", "../../windows/.env")) {
        val file = project.file(path)
        if (!file.exists()) continue
        file.readLines().map { it.trim() }
            .firstOrNull { it.startsWith("UNSPLASH_ACCESS_KEY=") }
            ?.substringAfter("=")?.trim()?.trim('"')
            ?.takeIf { it.isNotEmpty() }
            ?.let { return it }
    }
    logger.warn("No UNSPLASH_ACCESS_KEY: the app builds but can't fetch photos.")
    return ""
}

/** Release signing from `android/keystore.properties` (git-ignored): storeFile, storePassword, keyAlias, keyPassword. */
val signing = Properties().apply {
    val file = rootProject.file("keystore.properties")
    if (file.exists()) file.inputStream().use { load(it) }
}

android {
    namespace = "uz.akbarjon.oriel"
    compileSdk = 36

    defaultConfig {
        applicationId = "uz.akbarjon.oriel"
        minSdk = 26
        targetSdk = 36
        // One place for the version: `oriel.version` in gradle.properties (scripts/release.ps1 sets it).
        val version = providers.gradleProperty("oriel.version").get()
        val (major, minor, patch) = version.split('.').map { it.toInt() }
        versionCode = major * 10_000 + minor * 100 + patch
        versionName = version
        buildConfigField("String", "UNSPLASH_ACCESS_KEY", "\"${unsplashKey()}\"")
    }

    signingConfigs {
        if (signing.isNotEmpty()) {
            create("release") {
                storeFile = rootProject.file(signing.getProperty("storeFile"))
                storePassword = signing.getProperty("storePassword")
                keyAlias = signing.getProperty("keyAlias")
                keyPassword = signing.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
        debug {
            applicationIdSuffix = ".dev"
        }
        // What to put on a phone while developing: the debug app's id and key (Google sign-in keeps working), but not
        // debuggable, so ART optimizes it like a real install and Compose runs without its debug overhead. Debug builds
        // are only for the debugger. `gradlew installStaging`.
        create("staging") {
            initWith(getByName("debug"))
            isDebuggable = false
            matchingFallbacks += listOf("debug")
        }
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    packaging {
        resources.excludes += setOf("/META-INF/{AL2.0,LGPL2.1}", "DebugProbesKt.bin", "kotlin-tooling-metadata.json")
    }

    dependenciesInfo {
        includeInApk = false
        includeInBundle = false
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

// Deliberately small: no Firebase SDK, no image or HTTP library. Firebase and Unsplash are plain REST calls
// (HttpURLConnection + org.json from the platform), scheduling is AlarmManager + JobScheduler. Credential Manager is
// only for the Google account sheet at sign-in.
dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2025.10.01")
    implementation(composeBom)
    implementation("androidx.activity:activity-compose:1.11.0")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-core")
    implementation("androidx.credentials:credentials:1.5.0")
    implementation("androidx.credentials:credentials-play-services-auth:1.5.0")
    implementation("com.google.android.libraries.identity.googleid:googleid:1.1.1")
    // Real background blur behind the floating navigation bar (1.7.0 matches this Compose and Kotlin).
    implementation("dev.chrisbanes.haze:haze:1.7.0")
    debugImplementation("androidx.compose.ui:ui-tooling")
}
