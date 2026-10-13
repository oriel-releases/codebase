package uz.akbarjon.oriel.core

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONObject
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Position

/**
 * The crop each photo gets on this phone: the point chosen in the position editor, remembered per photo (the last few
 * hundred), else this phone's default (Smart or Center, in Settings). Only this phone's; nothing here is synced.
 */
object Positions {
    private const val KEEP = 300
    private val prefs by lazy { Oriel.app.getSharedPreferences("positions", Context.MODE_PRIVATE) }
    private val _version = MutableStateFlow(0)
    /** Bumped on every change, so previews crop again. */
    val version: StateFlow<Int> = _version.asStateFlow()

    /** This photo's own position, or null when it follows the default. */
    fun custom(photoId: String): Position.Focus? {
        val o = runCatching { JSONObject(prefs.getString("all", "{}")!!) }.getOrNull() ?: return null
        val v = o.optJSONArray(photoId) ?: return null
        return Position.Focus(v.optDouble(0, 0.5).toFloat(), v.optDouble(1, 0.5).toFloat())
    }

    fun default(): Position = if (Oriel.store.settings.value.photoPosition == "center") Position.Center else Position.Smart

    fun of(photoId: String): Position = custom(photoId) ?: default()

    /** The default changed: previews crop again. */
    fun touch() {
        _version.value++
    }

    /** Remembers [position] for this photo; Smart or Center means "use the default" (the custom point is forgotten). */
    @Synchronized fun set(photoId: String, position: Position) {
        val o = runCatching { JSONObject(prefs.getString("all", "{}")!!) }.getOrNull() ?: JSONObject()
        o.remove(photoId)
        if (position is Position.Focus) o.put(photoId, org.json.JSONArray().put(position.x.toDouble()).put(position.y.toDouble()))
        // Newest last; only the last few hundred are kept.
        val keys = o.keys().asSequence().toList()
        keys.take((keys.size - KEEP).coerceAtLeast(0)).forEach { o.remove(it) }
        prefs.edit().putString("all", o.toString()).apply()
        _version.value++
    }
}
