package uz.akbarjon.oriel.core

import android.content.Context
import android.content.Intent
import android.util.Log
import androidx.core.content.FileProvider
import java.io.File
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.launch
import org.json.JSONObject
import uz.akbarjon.oriel.BuildConfig
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Http

/** Where the updater is: shown in Settings, and as the navigation bar's Install item while an update waits. */
sealed interface UpdateState {
    data object Idle : UpdateState
    data object Checking : UpdateState
    data object UpToDate : UpdateState
    /** [progress] 0..1, or null while the size isn't known. */
    data class Downloading(val version: String, val progress: Float? = null) : UpdateState
    /** Downloaded: waits for "Install", which hands it to Android's installer. */
    data class ReadyToInstall(val version: String) : UpdateState
    data class Failed(val message: String) : UpdateState
    /** A newer version is out but this build can't install it (debug and staging, from the Updates page only). */
    data class Available(val version: String) : UpdateState
}

/**
 * Updates from GitHub Releases (`oriel-releases/android`, public, like the desktop apps' release repos), the same
 * moments as the Windows and Linux apps: when Oriel opens and after every new wallpaper.
 *
 * The APK is downloaded in the background, then waits for a tap on Install, which opens Android's own installer with it
 * (like Telegram). Oriel never installs itself: doing so closed it, and opening it again while the install was still
 * running closed it again at once. Android's installer asks, replaces the app and offers to open it.
 * Debug and staging builds (`.dev`) never update.
 */
object Updater {
    private const val TAG = "OrielUpdate"
    const val REPO = "oriel-releases/android"
    private const val LATEST = "https://api.github.com/repos/$REPO/releases/latest"
    /** GitHub allows 60 unauthenticated API calls an hour per address; a few checks an hour are plenty. */
    private const val MIN_INTERVAL_MS = 10 * 60_000L
    private const val APK_TYPE = "application/vnd.android.package-archive"

    private val _state = MutableStateFlow<UpdateState>(UpdateState.Idle)
    val state: StateFlow<UpdateState> = _state

    private val gate = Mutex()
    private var lastCheck = 0L
    /** The downloaded update waiting for Install. */
    private var ready: File? = null

    /** Real installs only: not debug builds, and not the `.dev` id (staging), which no release would match. */
    val enabled get() = !BuildConfig.DEBUG && !BuildConfig.APPLICATION_ID.endsWith(".dev")

    /**
     * Checks GitHub and, when there's a newer version, downloads it (then it waits for Install). Never throws.
     * [manual] is the Updates page: there a debug or staging build checks too (so the page looks as it does in a
     * release), but never downloads.
     */
    suspend fun check(context: Context, force: Boolean = false, manual: Boolean = false) {
        if (!enabled && !manual) return
        if (!gate.tryLock()) return
        try {
            val now = System.currentTimeMillis()
            if (!force && now - lastCheck < MIN_INTERVAL_MS) return
            lastCheck = now
            val dir = File(context.cacheDir, "updates").apply { mkdirs() }
            if (_state.value !is UpdateState.ReadyToInstall) _state.value = UpdateState.Checking

            val r = Http.get(LATEST, mapOf("Accept" to "application/vnd.github+json"))
            if (r.code == 404) { upToDate(dir); return }   // nothing published yet
            if (!r.ok) throw IllegalStateException("GitHub answered ${r.code}")
            val release = JSONObject(r.body)
            val version = release.getString("tag_name").removePrefix("v")
            if (!isNewer(version, BuildConfig.VERSION_NAME)) { upToDate(dir); return }
            if (!enabled) { _state.value = UpdateState.Available(version); return }
            val assets = release.getJSONArray("assets")
            val apk = (0 until assets.length()).map { assets.getJSONObject(it) }.firstOrNull { it.getString("name").endsWith(".apk") }
                ?: throw IllegalStateException("Release $version has no APK")

            val file = File(dir, "oriel-$version.apk")
            dir.listFiles()?.filter { it != file }?.forEach { it.delete() }   // older downloads
            if (file.length() != apk.getLong("size")) {
                _state.value = UpdateState.Downloading(version)
                var shown = -1
                Http.download(apk.getString("browser_download_url"), file) { done, total ->
                    val size = if (total > 0) total else apk.getLong("size")
                    val percent = if (size > 0) (done * 100 / size).toInt() else -1
                    if (percent != shown) { shown = percent; _state.value = UpdateState.Downloading(version, percent.takeIf { it >= 0 }?.div(100f)) }
                }
            }
            ready = file
            _state.value = UpdateState.ReadyToInstall(version)
        } catch (e: Exception) {
            Log.i(TAG, "Update check failed: ${e.message}")
            if (_state.value !is UpdateState.ReadyToInstall) _state.value = UpdateState.Failed("Couldn't check for updates")
        } finally {
            gate.unlock()
        }
    }

    /** This version is the latest: what an earlier update left behind goes. */
    private fun upToDate(dir: File) {
        ready = null
        dir.listFiles()?.forEach { it.delete() }
        _state.value = UpdateState.UpToDate
    }

    /**
     * "Install": opens Android's installer with the downloaded update. It stays ready afterwards, so if the installer is
     * backed out of, Install opens it again. Returns false when there's nothing downloaded (check again then).
     */
    fun install(context: Context): Boolean {
        val file = ready?.takeIf { it.exists() } ?: return false
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.shared", file)
        val intent = Intent(Intent.ACTION_VIEW).setDataAndType(uri, APK_TYPE)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        return try {
            context.startActivity(intent)
            true
        } catch (e: Exception) {
            Log.i(TAG, "Couldn't open the installer: ${e.message}")
            _state.value = UpdateState.Failed("Couldn't open the installer")
            false
        }
    }

    /** Kicks off a check without making the caller wait. */
    fun checkSoon(context: Context, force: Boolean = false, manual: Boolean = false) {
        val app = context.applicationContext
        Oriel.scope.launch { check(app, force, manual) }
    }

    /** "1.2.10" > "1.2.9"; anything after a "-" (like "-beta") is ignored. */
    internal fun isNewer(candidate: String, current: String): Boolean {
        fun parts(v: String) = v.substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 }
        val a = parts(candidate)
        val b = parts(current)
        for (i in 0 until maxOf(a.size, b.size)) {
            val x = a.getOrElse(i) { 0 }
            val y = b.getOrElse(i) { 0 }
            if (x != y) return x > y
        }
        return false
    }
}
