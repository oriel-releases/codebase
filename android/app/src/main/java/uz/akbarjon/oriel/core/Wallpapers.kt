package uz.akbarjon.oriel.core

import android.app.WallpaperManager
import android.content.Context
import android.hardware.display.DisplayManager
import android.util.DisplayMetrics
import android.util.Log
import android.view.Display
import java.io.File
import java.io.IOException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.CurrentWallpaper
import uz.akbarjon.oriel.data.Firebase
import uz.akbarjon.oriel.data.Http
import uz.akbarjon.oriel.data.OrielException
import uz.akbarjon.oriel.data.Photo
import uz.akbarjon.oriel.data.Position
import uz.akbarjon.oriel.data.Screen
import uz.akbarjon.oriel.data.Settings
import uz.akbarjon.oriel.data.Unsplash

/** What a change is doing right now, for the "Finding a photo… → Downloading… → Changing… → Syncing…" text. */
enum class Phase(val text: String) {
    Finding("Finding a photo…"),
    Downloading("Downloading…"),
    Changing("Changing…"),
    /** Sharing the new photo with the account's other devices. */
    Syncing("Syncing…"),
    /** Another device set a new photo; it is being fetched here. */
    Receiving("Receiving…"),
}

sealed interface ChangeResult {
    data class Applied(val photo: Photo) : ChangeResult
    /** Nothing to do (paused, following, already changed for this slot). */
    data object Skipped : ChangeResult
    data class Failed(val message: String, val transient: Boolean) : ChangeResult
}

/**
 * Changes the wallpaper. Only one change runs at a time; a second request while one runs is answered "busy".
 *
 * Light on purpose: one search request finds the photo, the CDN sends a JPEG already cropped to this screen (no
 * decoding or scaling on the phone), the system wallpaper service takes the file, and the file is deleted again.
 */
class Wallpapers(private val context: Context) {
    private val mutex = Mutex()
    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy.asStateFlow()

    private val _phase = MutableStateFlow<Phase?>(null)
    /** What the change under way is doing (null when nothing is), from any trigger: a button, the schedule, another device. */
    val phase: StateFlow<Phase?> = _phase.asStateFlow()

    /** For [Following]: "Receiving…" while the other device's photo is fetched (null clears it). */
    fun showPhase(phase: Phase?) {
        if (!_busy.value) _phase.value = phase
    }

    private val store get() = Oriel.store
    private val history get() = Oriel.history

    /** The schedule's change: only when one is due, not paused and not following anyone. */
    suspend fun scheduled(): ChangeResult {
        val s = store.settings.value
        if (!Oriel.auth.isSignedIn || s.paused || s.isFollowing) return ChangeResult.Skipped
        if (!Schedule.isDue(s, store.state.value.scheduleBaseline)) return ChangeResult.Skipped
        // Another device of the account may have changed it for this moment already: show theirs instead of a second pick.
        Oriel.following.check()
        if (!Schedule.isDue(s, store.state.value.scheduleBaseline)) return ChangeResult.Skipped
        // "First device wins": every device ticks at this moment; only the one that claims it picks a photo.
        if (claimSlot(Schedule.slotId(s, Schedule.dueTick(s))) == false) {
            store.updateState { it.copy(scheduleStartedAt = System.currentTimeMillis()) }
            Oriel.following.check() // its photo, if it's out already (else the doorbell or the next check brings it)
            return ChangeResult.Skipped
        }
        return run(manual = false) { pickAndApply(s) }
    }

    /** "Change now": a new photo right away, limited to [MAX_MANUAL_PER_HOUR] an hour (the Unsplash key is shared). */
    suspend fun changeNow(): ChangeResult {
        manualAvailableAt()?.let { at ->
            val minutes = ((at - System.currentTimeMillis()) / 60_000 + 1).coerceAtLeast(1)
            return ChangeResult.Failed("You can use Change now twice an hour. Try again in $minutes min.", transient = false)
        }
        val result = run(manual = true) { pickAndApply(store.settings.value) }
        if (result is ChangeResult.Applied) {
            val now = System.currentTimeMillis()
            store.updateState { st -> st.copy(manualChanges = st.manualChanges.filter { now - it < HOUR } + now) }
        }
        return result
    }

    /** When "Change now" works again, or null if it does now. */
    fun manualAvailableAt(): Long? {
        val now = System.currentTimeMillis()
        val recent = store.state.value.manualChanges.filter { now - it < HOUR }.sorted()
        return if (recent.size >= MAX_MANUAL_PER_HOUR) recent[recent.size - MAX_MANUAL_PER_HOUR] + HOUR else null
    }

    /**
     * Sets one particular photo: chosen in Search or History, shared by a link, or the one being followed.
     * [publish] shares it with the account's other devices (not while following: it came from them).
     */
    suspend fun apply(photo: Photo, topic: String?, publish: Boolean = true, track: Boolean = true): ChangeResult =
        run(manual = true) { setPhoto(photo, topic, publish, track) }

    /**
     * The position editor's "Set": the same photo again, cropped around [position], and remembered for it. Nothing else
     * changes (History, the other devices: the photo is the same one).
     */
    suspend fun reposition(photo: Photo, position: Position): ChangeResult = run(manual = true) {
        Positions.set(photo.id, position)
        val (w, h) = screenSize()
        val file = File(context.cacheDir, "wallpaper.jpg")
        try {
            _phase.value = Phase.Downloading
            Http.download(photo.sized(w, h, position = Positions.of(photo.id)), file)
            if (!looksLikeJpeg(file)) throw OrielException("The photo didn't download properly. Your wallpaper wasn't changed.", transient = true)
            _phase.value = Phase.Changing
            setWallpaper(file, store.settings.value.screen)
        } finally {
            file.delete()
        }
        photo
    }

    private suspend fun run(manual: Boolean, block: suspend () -> Photo): ChangeResult {
        if (!Oriel.auth.isSignedIn) return ChangeResult.Failed("Sign in to Oriel first.", transient = false)
        if (!mutex.tryLock()) return ChangeResult.Failed("A change is already in progress.", transient = true)
        _busy.value = true
        try {
            val photo = block()
            store.updateState { it.copy(lastAppliedAt = System.currentTimeMillis(), lastError = null, failures = 0) }
            return ChangeResult.Applied(photo)
        } catch (e: Exception) {
            val known = e as? OrielException
            val message = known?.message ?: when (e) {
                is IOException -> "Couldn't download the photo. Are you offline?"
                else -> "Something went wrong: ${e.message}"
            }
            val transient = known?.transient ?: (e is IOException)
            Log.w(TAG, "Change failed", e)
            // A failed "Change now" is shown right away; only the schedule's failures are kept for Home.
            if (!manual) store.updateState { it.copy(lastError = message, failures = it.failures + 1) }
            return ChangeResult.Failed(message, transient)
        } finally {
            _phase.value = null
            _busy.value = false
            mutex.unlock()
        }
    }

    private suspend fun pickAndApply(s: Settings): Photo {
        _phase.value = Phase.Finding
        if (s.favoritesOnly) {
            val current = history.latest()?.photo?.id
            val favorites = history.list(favoritesOnly = true)
            val pool = favorites.filter { it.photo.id != current }.ifEmpty { favorites }
            if (pool.isNotEmpty()) {
                val pick = pool.random()
                return setPhoto(pick.photo, pick.topic, publish = true, track = false)
            }
        }
        val (photo, topic) = findNew(s)
        return setPhoto(photo, topic, publish = true, track = true)
    }

    /**
     * "Match the time of day", the desktop apps' rule: darker photos (by Unsplash's average color) in the evening, at
     * night and in dark mode, brighter ones in the morning; the rest after them, so a change never fails because of it.
     */
    private fun byMood(photos: List<Photo>, s: Settings): List<Photo> {
        if (!s.matchTimeOfDay) return photos
        val hour = java.time.LocalTime.now().hour
        val dark = (context.resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK) ==
            android.content.res.Configuration.UI_MODE_NIGHT_YES
        val wantDark = hour >= 19 || hour < 6 || dark
        val wantBright = !wantDark && hour < 11
        if (!wantDark && !wantBright) return photos
        fun fits(p: Photo): Boolean {
            val l = luminance(p.color) ?: return true
            return if (wantDark) l < 0.18 else l > 0.3
        }
        return photos.filter(::fits) + photos.filterNot(::fits)
    }

    private fun luminance(hex: String?): Double? {
        val rgb = hex?.takeIf { it.length == 7 && it[0] == '#' }?.substring(1)?.toIntOrNull(16) ?: return null
        fun lin(c: Int): Double { val v = c / 255.0; return if (v <= 0.03928) v / 12.92 else Math.pow((v + 0.055) / 1.055, 2.4) }
        return 0.2126 * lin(rgb shr 16 and 0xFF) + 0.7152 * lin(rgb shr 8 and 0xFF) + 0.0722 * lin(rgb and 0xFF)
    }

    /** A photo never used before that suits this screen, from a few of the enabled interests. */
    private suspend fun findNew(s: Settings): Pair<Photo, String> {
        val size = screenSize()
        val topics = s.enabledQueries()
        val tried = mutableSetOf<String>()
        repeat(MAX_QUERIES) {
            val topic = topics.filter { it !in tried }.randomOrNull() ?: topics.randomOrNull() ?: FALLBACK_TOPIC
            tried += topic
            // A random one of the first few pages, so days differ but stay relevant.
            val page = (1..3).random()
            var found = Unsplash.search(topic, page).photos
            if (found.isEmpty() && page > 1) found = Unsplash.search(topic, 1).photos
            for (candidate in byMood(found.shuffled(), s)) {
                // Unsplash+ photos (plus.unsplash.com) need a paid license: only the free ones.
                if (!candidate.rawUrl.startsWith("https://images.unsplash.com/")) continue
                if (history.wasUsed(candidate.id)) continue
                if (candidate.width < size.first || candidate.height < size.second) continue
                if (s.skipLowLikes && candidate.likes < s.minLikes) continue
                return candidate to topic
            }
        }
        throw OrielException("Couldn't find a new photo for your interests. Try turning on more of them.")
    }

    private suspend fun setPhoto(photo: Photo, topic: String?, publish: Boolean, track: Boolean): Photo {
        val (w, h) = screenSize()
        val file = File(context.cacheDir, "wallpaper.jpg")
        var accent: String? = null
        try {
            _phase.value = Phase.Downloading
            Http.download(photo.sized(w, h, position = Positions.of(photo.id)), file)
            if (!looksLikeJpeg(file)) throw OrielException("The photo didn't download properly. Your wallpaper wasn't changed.", transient = true)
            _phase.value = Phase.Changing
            setWallpaper(file, store.settings.value.screen)
            accent = try {
                AccentPicker.fromFile(file)
            } catch (_: Exception) {
                null
            } ?: AccentPicker.fromAverage(photo.color)
        } finally {
            file.delete()
        }
        store.updateState { it.copy(accent = accent) }
        history.add(photo, topic)
        if (track) Unsplash.trackDownload(photo)
        if (publish) {
            _phase.value = Phase.Syncing
            publish(photo, topic, accent)
        }
        // Like the desktop apps: every new wallpaper is also a moment to look for an update.
        Updater.checkSoon(context)
        return photo
    }

    private fun setWallpaper(file: File, screen: Screen) {
        val manager = WallpaperManager.getInstance(context)
        if (!manager.isWallpaperSupported || !manager.isSetWallpaperAllowed)
            throw OrielException("This phone doesn't let apps change the wallpaper.")
        val which = when (screen) {
            Screen.Home -> WallpaperManager.FLAG_SYSTEM
            Screen.Lock -> WallpaperManager.FLAG_LOCK
            Screen.Both -> WallpaperManager.FLAG_SYSTEM or WallpaperManager.FLAG_LOCK
        }
        file.inputStream().buffered().use { manager.setStream(it, null, true, which) }
    }

    /** Shares the photo through `sync/current` (the extension and followers see it). Best effort: never fails a change. */
    private suspend fun publish(photo: Photo, topic: String?, accent: String?) {
        try {
            val tokens = Oriel.auth.tokens() ?: return
            Firebase.setCurrent(tokens, CurrentWallpaper(
                photoId = photo.id, imageUrl = photo.sized(400, quality = 80), pageUrl = photo.pageUrl,
                photographer = photo.photographer, photographerUrl = photo.photographerUrl, description = photo.description,
                topic = topic, color = accent ?: photo.color?.uppercase(), source = "android",
            ))
            // Your follow page shows it too.
            Sharing.showOnFollowPage(photo)
            // The doorbell: the account's devices listening hear about it now instead of at their next poll.
            try {
                Firebase.ring(tokens, photo.id, Oriel.auth.account.value?.sessionId.orEmpty())
            } catch (e: Exception) {
                Log.i(TAG, "Couldn't ring the other devices (they still poll): ${e.message}")
            }
        } catch (e: Exception) {
            Log.i(TAG, "Couldn't share the wallpaper: ${e.message}")
        }
    }

    /** Pixels of the whole screen in portrait (width, height), capped so downloads stay small on huge screens. */
    fun screenSize(): Pair<Int, Int> {
        val metrics = DisplayMetrics()
        @Suppress("DEPRECATION")
        context.getSystemService(DisplayManager::class.java).getDisplay(Display.DEFAULT_DISPLAY).getRealMetrics(metrics)
        var w = minOf(metrics.widthPixels, metrics.heightPixels).coerceAtLeast(720)
        var h = maxOf(metrics.widthPixels, metrics.heightPixels).coerceAtLeast(1280)
        if (w > MAX_WIDTH) {
            h = h * MAX_WIDTH / w; w = MAX_WIDTH
        }
        return w to h
    }

    private fun looksLikeJpeg(file: File): Boolean {
        if (file.length() < 1024) return false
        val head = ByteArray(2)
        file.inputStream().use { it.read(head) }
        return head[0] == 0xFF.toByte() && head[1] == 0xD8.toByte()
    }

    companion object {
        private const val TAG = "Oriel"
        private const val MAX_QUERIES = 3
        private const val MAX_MANUAL_PER_HOUR = 2
        private const val HOUR = 3_600_000L
        private const val MAX_WIDTH = 2160
        const val FALLBACK_TOPIC = "landscape"
    }
}
