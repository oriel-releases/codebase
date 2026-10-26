package uz.akbarjon.oriel.core

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONObject
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Position

/**
 * The crop each photo gets on this phone: what was picked for it in the position editor (a point, or Smart or Center),
 * remembered per photo (the last few hundred), else this phone's default (Smart or Center, in Settings). Only this
 * phone's; nothing here is synced.
 */
object Positions {
    private const val KEEP = 300
    private val prefs by lazy { Oriel.app.getSharedPreferences("positions", Context.MODE_PRIVATE) }
    private val _version = MutableStateFlow(0)
    /** Bumped on every change, so previews crop again. */
    val version: StateFlow<Int> = _version.asStateFlow()

    /** What was picked for this photo (a point, Smart or Center), or null when it follows the default. */
    fun picked(photoId: String): Position? {
        val o = runCatching { JSONObject(prefs.getString("all", "{}")!!) }.getOrNull() ?: return null
        o.optJSONArray(photoId)?.let { v -> return Position.Focus(v.optDouble(0, 0.5).toFloat(), v.optDouble(1, 0.5).toFloat()) }
        return when (o.optString(photoId)) {
            "smart" -> Position.Smart
            "center" -> Position.Center
            else -> null
        }
    }

    fun default(): Position = if (Oriel.store.settings.value.photoPosition == "center") Position.Center else Position.Smart

    fun of(photoId: String): Position = picked(photoId) ?: default()

    /** The default changed: previews crop again. */
    fun touch() {
        _version.value++
    }

    /**
     * Remembers [position] for this photo. Smart or Center is kept only when it isn't the default already (so a photo
     * set to Smart stays Smart when the default is Center); the same as the default, the photo just follows it.
     */
    @Synchronized fun set(photoId: String, position: Position) {
        val o = runCatching { JSONObject(prefs.getString("all", "{}")!!) }.getOrNull() ?: JSONObject()
        o.remove(photoId)
        when {
            position is Position.Focus -> o.put(photoId, org.json.JSONArray().put(position.x.toDouble()).put(position.y.toDouble()))
            position != default() -> o.put(photoId, if (position == Position.Center) "center" else "smart")
        }
        // Newest last; only the last few hundred are kept.
        val keys = o.keys().asSequence().toList()
        keys.take((keys.size - KEEP).coerceAtLeast(0)).forEach { o.remove(it) }
        prefs.edit().putString("all", o.toString()).apply()
        _version.value++
    }
}
