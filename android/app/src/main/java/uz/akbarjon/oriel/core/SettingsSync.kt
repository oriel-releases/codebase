package uz.akbarjon.oriel.core

import android.util.Log
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONArray
import org.json.JSONObject
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Firebase
import uz.akbarjon.oriel.data.FirebaseException
import uz.akbarjon.oriel.data.Interest
import uz.akbarjon.oriel.data.Settings

/** The settings every device of an account shares (`users/{uid}/sync/settings`). Device-only ones stay on the phone. */
data class SyncedSettings(
    val dailyTime: String,
    val timeZone: String,
    val intervalMinutes: Int,
    val paused: Boolean,
    val favoritesOnly: Boolean,
    val skipLowLikes: Boolean,
    val minLikes: Int,
    val interests: List<Interest>,
    val matchTimeOfDay: Boolean = true,
) {
    fun applyTo(s: Settings) = s.copy(
        dailyTime = dailyTime, timeZone = timeZone, intervalMinutes = intervalMinutes, paused = paused,
        favoritesOnly = favoritesOnly, skipLowLikes = skipLowLikes, minLikes = minLikes, matchTimeOfDay = matchTimeOfDay,
        interests = Settings.updateBuiltIn(interests),
    )

    /** The schedule part: changing it starts the schedule fresh (a moment that just moved isn't "missed"). */
    fun sameSchedule(o: SyncedSettings) =
        dailyTime == o.dailyTime && timeZone == o.timeZone && intervalMinutes == o.intervalMinutes && paused == o.paused

    fun fingerprint(): String = JSONObject()
        .put("d", dailyTime).put("z", timeZone).put("i", intervalMinutes).put("p", paused).put("f", favoritesOnly)
        .put("s", skipLowLikes).put("m", minLikes).put("tod", matchTimeOfDay)
        .put("t", JSONArray(interests.map { "${it.query}|${it.enabled}|${it.isCustom}" })).toString()

    companion object {
        fun from(s: Settings) = SyncedSettings(
            s.dailyTime, s.timeZone, s.intervalMinutes, s.paused, s.favoritesOnly, s.skipLowLikes, s.minLikes,
            s.interests.filter { it.query.isNotBlank() }, s.matchTimeOfDay,
        )
    }
}

/**
 * Keeps the shared settings the same on every device, like the desktop apps' SettingsSync: a change here is written to
 * `sync/settings` and rings the other devices; a ring from another device ([Pulse]), opening the app, or the
 * background job brings theirs here. Last write wins. A phone signing in adopts the account's settings, or uploads its
 * own when the account has none yet.
 */
object SettingsSync {
    private const val TAG = "OrielSettingsSync"
    private val gate = Mutex()
    private var watcher: Job? = null

    /** Uploads local changes as they happen (once they settle). */
    @OptIn(FlowPreview::class)
    fun start() {
        if (watcher?.isActive == true) return
        watcher = Oriel.scope.launch {
            var last = SyncedSettings.from(Oriel.store.settings.value)
            Oriel.store.settings.map { SyncedSettings.from(it) }.distinctUntilChanged().drop(1)
                .debounce(1_200)
                .collect { now ->
                    val before = last
                    last = now
                    if (!before.sameSchedule(now)) Scheduler.restart(Oriel.app)
                    // Came from the account (a pull): nothing to send back.
                    if (now.fingerprint() != Oriel.store.state.value.syncedSettingsHash) push()
                }
        }
    }

    suspend fun push() = gate.withLock {
        val tokens = Oriel.auth.tokens() ?: return@withLock
        val mine = SyncedSettings.from(Oriel.store.settings.value)
        val device = Oriel.auth.account.value?.sessionId.orEmpty()
        val at = System.currentTimeMillis()
        try {
            Firebase.setSyncedSettings(tokens, mine, device, at)
            Oriel.store.updateState { it.copy(syncedSettingsHash = mine.fingerprint(), settingsSyncedAt = at, settingsDirty = false) }
            Log.i(TAG, "Shared your settings with your other devices")
            runCatching { Firebase.ringSettings(tokens, device) }
        } catch (e: FirebaseException) {
            // Offline: remembered, and sent the next time the phone talks to the account.
            Oriel.store.updateState { it.copy(settingsDirty = true) }
            Log.i(TAG, "Couldn't share your settings yet: ${e.message}")
        }
    }

    /** Brings the account's settings here when they're newer (another device changed them). */
    suspend fun pull() {
        val push = gate.withLock {
            val tokens = Oriel.auth.tokens() ?: return@withLock false
            val remote = try {
                Firebase.getSyncedSettings(tokens)
            } catch (e: FirebaseException) {
                Log.i(TAG, "Couldn't read the shared settings: ${e.message}")
                return@withLock false
            } ?: return@withLock true // the first device: the account takes this one's settings
            val (theirs, updatedAt) = remote
            val state = Oriel.store.state.value
            val mine = SyncedSettings.from(Oriel.store.settings.value)
            when {
                theirs.fingerprint() == mine.fingerprint() -> {
                    Oriel.store.updateState { it.copy(syncedSettingsHash = mine.fingerprint(), settingsSyncedAt = updatedAt, settingsDirty = false) }
                    false
                }
                // Changed here while offline, after the account's last change: the phone's is the newer one.
                state.settingsDirty && state.settingsSyncedAt >= updatedAt -> true
                else -> {
                    Oriel.store.updateState { it.copy(syncedSettingsHash = theirs.fingerprint(), settingsSyncedAt = updatedAt, settingsDirty = false) }
                    Oriel.store.update { theirs.applyTo(it) }
                    if (!theirs.sameSchedule(mine)) Scheduler.restart(Oriel.app)
                    Log.i(TAG, "Took the settings changed on another device")
                    false
                }
            }
        }
        if (push) push()
    }
}

/**
 * "First device wins" for scheduled changes: every device ticks at the same moments ([Schedule]), and the first to
 * claim a moment (`users/{uid}/sync/slot`) picks the photo; the others show that one (it comes through `sync/current`).
 * True: this phone takes it. False: another device already did. Null: unknown (offline), go ahead.
 */
suspend fun claimSlot(slotId: String): Boolean? = try {
    val tokens = Oriel.auth.tokens()
    if (tokens == null) null else Firebase.claimSlot(tokens, slotId, Oriel.auth.account.value?.sessionId.orEmpty())
} catch (e: FirebaseException) {
    Log.i("OrielSlot", "Couldn't claim $slotId (${e.message}); changing anyway")
    null
}
