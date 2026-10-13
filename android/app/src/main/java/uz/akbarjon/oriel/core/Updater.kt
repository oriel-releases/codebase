package uz.akbarjon.oriel.core

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.os.Build
import android.util.Log
import java.io.File
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.launch
import org.json.JSONObject
import uz.akbarjon.oriel.BuildConfig
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Http

/** Where the updater is: shown in Settings, and on Home when an update waits for a tap. */
sealed interface UpdateState {
    data object Idle : UpdateState
    data object Checking : UpdateState
    data object UpToDate : UpdateState
    data class Downloading(val version: String) : UpdateState
    data class Installing(val version: String) : UpdateState
    /** Android wants the person to confirm (the first update, or "install unknown apps" isn't allowed yet). */
    data class NeedsConfirm(val version: String) : UpdateState
    data class Failed(val message: String) : UpdateState
}

/**
 * Self-updates from GitHub Releases (`oriel-releases/android`, public, like the desktop apps' release repos), the
 * same moments as the Windows and Linux apps: when Oriel opens and after every new wallpaper.
 *
 * The APK is downloaded in the background and handed to PackageInstaller. On Android 12+ an app may update itself
 * without asking once it is the installer of record (after the first update it installed itself), so from then on
 * updates are silent. Otherwise Android asks, and the confirmation waits for the next time the app is open.
 * Debug and staging builds (`.dev`) never update.
 */
object Updater {
    private const val TAG = "OrielUpdate"
    const val REPO = "oriel-releases/android"
    private const val LATEST = "https://api.github.com/repos/$REPO/releases/latest"
    /** GitHub allows 60 unauthenticated API calls an hour per address; a few checks an hour are plenty. */
    private const val MIN_INTERVAL_MS = 10 * 60_000L
    private const val ACTION_RESULT = "uz.akbarjon.oriel.UPDATE_RESULT"

    private val _state = MutableStateFlow<UpdateState>(UpdateState.Idle)
    val state: StateFlow<UpdateState> = _state

    private val gate = Mutex()
    private var lastCheck = 0L
    /** The confirmation Android asked for, kept until the app is in front to show it. */
    private var pendingConfirm: Intent? = null

    /** Real installs only: not debug builds, and not the `.dev` id (staging), which no release would match. */
    val enabled get() = !BuildConfig.DEBUG && !BuildConfig.APPLICATION_ID.endsWith(".dev")

    /** Checks GitHub and, when there's a newer version, downloads and installs it. Never throws. */
    suspend fun check(context: Context, force: Boolean = false) {
        if (!enabled) return
        if (!gate.tryLock()) return
        try {
            val now = System.currentTimeMillis()
            if (!force && now - lastCheck < MIN_INTERVAL_MS) return
            lastCheck = now
            if (_state.value !is UpdateState.NeedsConfirm) _state.value = UpdateState.Checking

            val r = Http.get(LATEST, mapOf("Accept" to "application/vnd.github+json"))
            if (r.code == 404) { _state.value = UpdateState.UpToDate; return }   // nothing published yet
            if (!r.ok) throw IllegalStateException("GitHub answered ${r.code}")
            val release = JSONObject(r.body)
            val version = release.getString("tag_name").removePrefix("v")
            if (!isNewer(version, BuildConfig.VERSION_NAME)) { _state.value = UpdateState.UpToDate; return }
            val assets = release.getJSONArray("assets")
            val apk = (0 until assets.length()).map { assets.getJSONObject(it) }.firstOrNull { it.getString("name").endsWith(".apk") }
                ?: throw IllegalStateException("Release $version has no APK")

            val dir = File(context.cacheDir, "updates").apply { mkdirs() }
            val file = File(dir, "oriel-$version.apk")
            dir.listFiles()?.filter { it != file }?.forEach { it.delete() }   // older downloads
            if (file.length() != apk.getLong("size")) {
                _state.value = UpdateState.Downloading(version)
                Http.download(apk.getString("browser_download_url"), file)
            }
            _state.value = UpdateState.Installing(version)
            install(context, file, version)
        } catch (e: Exception) {
            Log.i(TAG, "Update check failed: ${e.message}")
            if (_state.value !is UpdateState.NeedsConfirm) _state.value = UpdateState.Failed("Couldn't check for updates")
        } finally {
            gate.unlock()
        }
    }

    private fun install(context: Context, apk: File, version: String) {
        val installer = context.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(context.packageName)
            if (Build.VERSION.SDK_INT >= 31) setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
        }
        val id = installer.createSession(params)
        installer.openSession(id).use { session ->
            apk.inputStream().use { input ->
                session.openWrite("oriel.apk", 0, apk.length()).use { out ->
                    input.copyTo(out, 256 * 1024)
                    session.fsync(out)
                }
            }
            val callback = PendingIntent.getBroadcast(
                context, id, Intent(ACTION_RESULT).setPackage(context.packageName).putExtra("version", version),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE,
            )
            session.commit(callback.intentSender)
        }
    }

    /** PackageInstaller's answer: done (the app restarts as the new version), or Android wants a confirmation. */
    internal fun onResult(context: Context, intent: Intent) {
        val version = intent.getStringExtra("version").orEmpty()
        when (intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                @Suppress("DEPRECATION")
                pendingConfirm = (if (Build.VERSION.SDK_INT >= 33) intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent::class.java)
                else intent.getParcelableExtra(Intent.EXTRA_INTENT))?.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                _state.value = UpdateState.NeedsConfirm(version)
                if (foreground) confirm(context)
            }
            PackageInstaller.STATUS_SUCCESS -> _state.value = UpdateState.UpToDate
            else -> {
                Log.i(TAG, "Install failed: ${intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)}")
                _state.value = UpdateState.Failed("Couldn't install the update")
            }
        }
    }

    /** Set by the activity: the confirmation can only be shown while the app is on screen. */
    @Volatile var foreground = false

    /** Shows Android's install confirmation, if one is waiting. Returns false when there's none (check again then). */
    fun confirm(context: Context): Boolean {
        val intent = pendingConfirm ?: return false
        pendingConfirm = null   // shown once; the next check asks again if it was dismissed
        return try {
            context.startActivity(intent)
            true
        } catch (e: Exception) {
            Log.i(TAG, "Couldn't show the install confirmation: ${e.message}")
            false
        }
    }

    /** Kicks off a check without making the caller wait. */
    fun checkSoon(context: Context, force: Boolean = false) {
        val app = context.applicationContext
        Oriel.scope.launch { check(app, force) }
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

/** Receives PackageInstaller's result for an update. */
class UpdateReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) = Updater.onResult(context, intent)
}
