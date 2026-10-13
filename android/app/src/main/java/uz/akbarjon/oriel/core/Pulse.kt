package uz.akbarjon.oriel.core

import android.util.Log
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Firebase
import uz.akbarjon.oriel.data.FirebaseTokens

/**
 * While Oriel is on screen, hears within a second that another device of the account set a new wallpaper: one
 * Server-Sent Events connection to the account's doorbell (`pulse/{uid}` in the Realtime Database), which every device
 * rings right after publishing `sync/current`. In the background the phone doesn't keep a connection (battery); the
 * 15-minute job and opening the app catch up instead.
 */
object Pulse {
    private const val TAG = "OrielPulse"
    private var job: Job? = null
    @Volatile private var connection: HttpURLConnection? = null

    fun start() {
        if (job?.isActive == true) return
        job = Oriel.scope.launch(Dispatchers.IO) { run() }
    }

    fun stop() {
        job?.cancel()
        job = null
        // A blocked read doesn't notice the cancel: closing the socket ends it.
        connection?.let { c -> Thread { runCatching { c.disconnect() } }.start() }
    }

    private suspend fun run() {
        var wait = 2_000L
        while (kotlinx.coroutines.currentCoroutineContext().isActive) {
            try {
                Oriel.auth.account.first { it != null }
                val tokens = Oriel.auth.tokens()
                if (tokens == null) {
                    delay(30_000)
                    continue
                }
                listen(tokens)
                wait = 2_000L // ended normally (the hourly token expired): reconnect at once
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.i(TAG, "Live sync connection: ${e.message}")
            }
            delay(wait)
            wait = (wait * 2).coerceAtMost(120_000L)
        }
    }

    private suspend fun listen(tokens: FirebaseTokens) = withContext(Dispatchers.IO) {
        val conn = (URL(Firebase.pulseUrl(tokens)).openConnection() as HttpURLConnection).apply {
            setRequestProperty("Accept", "text/event-stream")
            useCaches = false
            connectTimeout = 8_000
            readTimeout = 75_000 // the server sends a keep-alive every 30 s; silence longer than this is a dead line
        }
        connection = conn
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            Log.i(TAG, "Listening for wallpapers from your other devices")
            conn.inputStream.bufferedReader().use { reader ->
                var name: String? = null
                while (isActive) {
                    val line = reader.readLine() ?: break
                    when {
                        line.startsWith("event:") -> name = line.substring(6).trim()
                        line.startsWith("data:") -> when (name) {
                            "put", "patch" -> onPulse(line.substring(5).trim(), initial = name == "put")
                            "auth_revoked", "cancel" -> return@use
                        }
                    }
                }
            }
        } finally {
            connection = null
            conn.disconnect()
        }
    }

    /**
     * The first event ("put") is the doorbell as it is now, catching up: both the photo and the settings are checked.
     * Later rings are PATCHes with either photo fields or settings fields; this phone's own are ignored.
     */
    private fun onPulse(json: String, initial: Boolean) {
        val data = runCatching { JSONObject(json).optJSONObject("data") }.getOrNull() ?: return
        val me = Oriel.auth.account.value?.sessionId
        val photo = data.has("photoId") && (initial || data.optString("device") != me)
        val prefs = data.has("settingsAt") && (initial || data.optString("settingsDevice") != me)
        val stars = data.has("favoritesAt") && (initial || data.optString("favoritesDevice") != me)
        if (stars) Oriel.scope.launch { FavoritesSync.pull() }
        if (!photo && !prefs) return
        Oriel.scope.launch {
            // Settings first: a new schedule decides whether the photo check should change anything.
            if (prefs) SettingsSync.pull()
            if (photo) Oriel.following.check()
        }
    }
}
