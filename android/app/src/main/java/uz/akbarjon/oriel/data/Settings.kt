package uz.akbarjon.oriel.data

import android.content.Context
import java.time.ZoneId
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONArray
import org.json.JSONObject

/** One interest (topic chip): a built-in like "ocean" or the user's own words. */
data class Interest(val query: String, val enabled: Boolean, val isCustom: Boolean = false)

/** Which screen gets the photo. Device-specific: not synced. */
enum class Screen { Home, Lock, Both }

/** Where this phone's wallpaper comes from. */
enum class Source {
    /** Oriel picks photos from the interests, on the schedule, shared with every device of the account. */
    Own,
    /** A friend's wallpaper, followed with their code. */
    Friend,
}

data class Settings(
    val interests: List<Interest> = BUILT_IN.map { Interest(it, it in DEFAULT_ON) },
    /** Time of day for the daily change, "HH:mm", in [timeZone]. */
    val dailyTime: String = "09:00",
    /** IANA zone the schedule is anchored in, so every device computes the same moments. */
    val timeZone: String = ZoneId.systemDefault().id,
    /** Minutes between changes; 0 means once a day at [dailyTime]. */
    val intervalMinutes: Int = 0,
    val favoritesOnly: Boolean = false,
    val skipLowLikes: Boolean = false,
    val minLikes: Int = 300,
    /** This phone's default crop for new photos: "smart" (Unsplash's faces and detail) or "center". Not synced. */
    val photoPosition: String = "smart",
    /** Darker photos in the evening and at night (and in dark mode), brighter ones in the morning. Like the desktop apps. */
    val matchTimeOfDay: Boolean = true,
    val paused: Boolean = false,
    val screen: Screen = Screen.Both,
    val source: Source = Source.Own,
    val followUid: String? = null,
    val followName: String? = null,
    /** The followed friend's Google picture, for the Friends page. */
    val followPicture: String? = null,
    /** Your own follow code, once you share your wallpaper (Friends). */
    val myFollowCode: String? = null,
) {
    fun enabledQueries(): List<String> =
        interests.filter { it.enabled && it.query.isNotBlank() }.map { it.query.trim() }.distinctBy { it.lowercase() }

    fun zone(): ZoneId = runCatching { ZoneId.of(timeZone) }.getOrDefault(ZoneId.systemDefault())

    /** Following someone (the computer or a friend): the own schedule is off. */
    val isFollowing get() = source != Source.Own

    companion object {
        /** The same list as the desktop apps (OrielSettings.BuiltInTopics), so interests mean the same on every device. */
        val BUILT_IN = listOf(
            "nature", "mountains", "ocean", "beach", "lakes", "waterfalls", "forest", "autumn",
            "flowers", "desert", "snow", "northern lights", "night sky", "space", "sunset", "cities",
            "city at night", "architecture", "japan", "islands", "wildlife", "countryside", "aerial", "fog",
        )
        private val DEFAULT_ON = setOf("nature", "mountains", "ocean", "space", "architecture")

        val EMOJI = mapOf(
            "nature" to "🌿", "mountains" to "🏔️", "ocean" to "🌊", "beach" to "🏖️", "lakes" to "🛶",
            "waterfalls" to "💧", "forest" to "🌲", "autumn" to "🍂", "flowers" to "🌸", "desert" to "🏜️",
            "snow" to "❄️", "northern lights" to "🌌", "night sky" to "🌃", "space" to "🪐", "sunset" to "🌅",
            "cities" to "🏙️", "city at night" to "🌉", "architecture" to "🏛️", "japan" to "⛩️", "islands" to "🏝️",
            "wildlife" to "🦊", "countryside" to "🌾", "aerial" to "🚁", "fog" to "🌫️",
        )

        /**
         * A saved list brought up to date with [BUILT_IN] (like the desktop apps): new ones are added (off), ones no
         * longer built in go, except that one that was on stays, as the user's own. Own interests and choices are kept.
         */
        fun updateBuiltIn(saved: List<Interest>): List<Interest> {
            val clean = saved.filter { it.query.isNotBlank() }
            val builtIn = BUILT_IN.map { name ->
                Interest(name, clean.firstOrNull { !it.isCustom && it.query.equals(name, ignoreCase = true) }?.enabled ?: false)
            }
            val own = clean.filter { it.isCustom } +
                clean.filter { !it.isCustom && it.enabled && BUILT_IN.none { b -> b.equals(it.query, ignoreCase = true) } }
                    .map { it.copy(isCustom = true) }
            return builtIn + own
        }
    }
}

/** Runtime state, not edited by the user. Times are epoch milliseconds. */
data class AppState(
    val lastAppliedAt: Long = 0,
    /**
     * When the schedule last started fresh: sign-in, or the user editing it (time, interval, zone, unpausing). Ticks
     * before this moment aren't "missed", so such edits never change the wallpaper on their own.
     */
    val scheduleStartedAt: Long = 0,
    val lastError: String? = null,
    val failures: Int = 0,
    /** Successful "Change now" presses in the last hour, for its limit. */
    val manualChanges: List<Long> = emptyList(),
    /** The followed photo already applied, so it isn't set again. */
    val followedPhotoId: String? = null,
    /** Why following doesn't work right now, for the UI. */
    val followProblem: String? = null,
    /** When the account and session were last checked with Google. */
    val lastValidatedAt: Long = 0,
    /** The current wallpaper's accent color ("#RRGGBB"), picked from the photo; the app tints itself with it. */
    val accent: String? = null,
    /** The shared settings as last sent or received (to tell a real change from an echo), and when. */
    val syncedSettingsHash: String? = null,
    val settingsSyncedAt: Long = 0,
    /** Changed here but not sent yet (offline). */
    val settingsDirty: Boolean = false,
    /** The newest favorite change taken from the account (the next sync asks only for later ones). */
    val favoritesSyncedAt: Long = 0,
    /** The first-run tour was done or skipped (phones that already had wallpapers count as done). */
    val onboardingDone: Boolean = false,
) {
    /** The moment the schedule counts from: the last change, or a fresh start after it. */
    val scheduleBaseline get() = maxOf(lastAppliedAt, scheduleStartedAt)
}

/** Settings and state, kept as two small JSON blobs and handed to the UI as flows. */
class Store(context: Context) {
    private val prefs = context.getSharedPreferences("oriel", Context.MODE_PRIVATE)

    private val _settings = MutableStateFlow(readSettings())
    val settings: StateFlow<Settings> = _settings.asStateFlow()

    private val _state = MutableStateFlow(readState())
    val state: StateFlow<AppState> = _state.asStateFlow()

    @Synchronized fun update(change: (Settings) -> Settings): Settings {
        val next = change(_settings.value)
        _settings.value = next
        prefs.edit().putString("settings", write(next).toString()).apply()
        return next
    }

    @Synchronized fun updateState(change: (AppState) -> AppState): AppState {
        val next = change(_state.value)
        _state.value = next
        prefs.edit().putString("state", write(next).toString()).apply()
        return next
    }

    /** Back to defaults on sign-out (the next account starts fresh). */
    @Synchronized fun reset() {
        prefs.edit().clear().apply()
        _settings.value = Settings()
        _state.value = AppState()
    }

    private fun readSettings(): Settings {
        val o = prefs.getString("settings", null)?.let { runCatching { JSONObject(it) }.getOrNull() } ?: return Settings()
        val d = Settings()
        val interests = o.optJSONArray("interests")?.let { a ->
            (0 until a.length()).map { a.getJSONObject(it) }.map { Interest(it.getString("query"), it.optBoolean("enabled"), it.optBoolean("isCustom")) }
        } ?: d.interests
        return Settings(
            interests = Settings.updateBuiltIn(interests),
            dailyTime = o.optString("dailyTime", d.dailyTime),
            timeZone = o.optString("timeZone", d.timeZone),
            intervalMinutes = o.optInt("intervalMinutes", d.intervalMinutes),
            favoritesOnly = o.optBoolean("favoritesOnly", d.favoritesOnly),
            skipLowLikes = o.optBoolean("skipLowLikes", d.skipLowLikes),
            minLikes = o.optInt("minLikes", d.minLikes),
            matchTimeOfDay = o.optBoolean("matchTimeOfDay", d.matchTimeOfDay),
            photoPosition = o.optString("photoPosition", d.photoPosition),
            paused = o.optBoolean("paused", d.paused),
            screen = enumOr(o.optString("screen"), d.screen),
            source = enumOr(o.optString("source"), d.source),
            followUid = o.optString("followUid").ifEmpty { null },
            followName = o.optString("followName").ifEmpty { null },
            followPicture = o.optString("followPicture").ifEmpty { null },
            myFollowCode = o.optString("myFollowCode").ifEmpty { null },
        )
    }

    private fun write(s: Settings) = JSONObject()
        .put("interests", JSONArray(s.interests.map { JSONObject().put("query", it.query).put("enabled", it.enabled).put("isCustom", it.isCustom) }))
        .put("dailyTime", s.dailyTime)
        .put("timeZone", s.timeZone)
        .put("intervalMinutes", s.intervalMinutes)
        .put("favoritesOnly", s.favoritesOnly)
        .put("skipLowLikes", s.skipLowLikes)
        .put("minLikes", s.minLikes)
        .put("matchTimeOfDay", s.matchTimeOfDay)
        .put("photoPosition", s.photoPosition)
        .put("paused", s.paused)
        .put("screen", s.screen.name)
        .put("source", s.source.name)
        .put("followUid", s.followUid ?: "")
        .put("followName", s.followName ?: "")
        .put("followPicture", s.followPicture ?: "")
        .put("myFollowCode", s.myFollowCode ?: "")

    private fun readState(): AppState {
        val o = prefs.getString("state", null)?.let { runCatching { JSONObject(it) }.getOrNull() } ?: return AppState()
        return AppState(
            lastAppliedAt = o.optLong("lastAppliedAt"),
            scheduleStartedAt = o.optLong("scheduleStartedAt"),
            lastError = o.optString("lastError").ifEmpty { null },
            failures = o.optInt("failures"),
            manualChanges = o.optJSONArray("manualChanges")?.let { a -> (0 until a.length()).map { a.getLong(it) } } ?: emptyList(),
            followedPhotoId = o.optString("followedPhotoId").ifEmpty { null },
            followProblem = o.optString("followProblem").ifEmpty { null },
            lastValidatedAt = o.optLong("lastValidatedAt"),
            accent = o.optString("accent").ifEmpty { null },
            syncedSettingsHash = o.optString("syncedSettingsHash").ifEmpty { null },
            settingsSyncedAt = o.optLong("settingsSyncedAt"),
            settingsDirty = o.optBoolean("settingsDirty"),
            favoritesSyncedAt = o.optLong("favoritesSyncedAt"),
            onboardingDone = o.optBoolean("onboardingDone"),
        )
    }

    private fun write(s: AppState) = JSONObject()
        .put("lastAppliedAt", s.lastAppliedAt)
        .put("scheduleStartedAt", s.scheduleStartedAt)
        .put("lastError", s.lastError ?: "")
        .put("failures", s.failures)
        .put("manualChanges", JSONArray(s.manualChanges))
        .put("followedPhotoId", s.followedPhotoId ?: "")
        .put("followProblem", s.followProblem ?: "")
        .put("lastValidatedAt", s.lastValidatedAt)
        .put("accent", s.accent ?: "")
        .put("syncedSettingsHash", s.syncedSettingsHash ?: "")
        .put("settingsSyncedAt", s.settingsSyncedAt)
        .put("settingsDirty", s.settingsDirty)
        .put("favoritesSyncedAt", s.favoritesSyncedAt)
        .put("onboardingDone", s.onboardingDone)

    private inline fun <reified T : Enum<T>> enumOr(name: String, fallback: T): T =
        enumValues<T>().firstOrNull { it.name == name } ?: fallback
}
