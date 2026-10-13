package uz.akbarjon.oriel

import android.app.Application
import android.content.Context
import android.net.http.HttpResponseCache
import java.io.File
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import uz.akbarjon.oriel.core.Following
import uz.akbarjon.oriel.core.Wallpapers
import uz.akbarjon.oriel.data.Auth
import uz.akbarjon.oriel.data.History
import uz.akbarjon.oriel.data.Store

/** The app's few long-lived objects, created on first use (a background job only builds what it touches). */
object Oriel {
    lateinit var app: Context
        private set

    val store by lazy { Store(app) }
    val history by lazy { History(app) }
    val auth by lazy { Auth(app) }
    val wallpapers by lazy { Wallpapers(app) }
    val following by lazy { Following() }

    /** For work that must finish even if the screen that started it goes away ("Change now", signing in). */
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    fun init(context: Context) {
        app = context.applicationContext
    }
}

class OrielApp : Application() {
    override fun onCreate() {
        super.onCreate()
        Oriel.init(this)
        // Thumbnails and API answers are cached on disk by HTTP rules (Unsplash images cache for a long time).
        try {
            HttpResponseCache.install(File(cacheDir, "http"), 24L * 1024 * 1024)
        } catch (_: Exception) {
        }
    }
}
