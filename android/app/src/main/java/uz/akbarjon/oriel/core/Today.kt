package uz.akbarjon.oriel.core

import java.io.File
import java.time.LocalDate
import org.json.JSONObject
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Http
import uz.akbarjon.oriel.data.Photo

/**
 * Wallpaper of the day, the same photo as oriel.akbarjon.uz/today (and the desktop apps' Home card). The website
 * publishes the list (`/today.json`); the pick is the website's rule: whole days since 1 January 1970 in local time,
 * going round the list. The list is kept on the phone and fetched again every few days.
 */
object Today {
    private const val URL = "https://oriel.akbarjon.uz/today.json"
    private const val REFRESH_MS = 3L * 24 * 3_600_000

    suspend fun photo(): Photo? {
        val list = load()
        if (list.isEmpty()) return null
        val day = LocalDate.now().toEpochDay()
        return list[(((day % list.size) + list.size) % list.size).toInt()]
    }

    private suspend fun load(): List<Photo> {
        val file = File(Oriel.app.filesDir, "today.json")
        if (!file.exists() || System.currentTimeMillis() - file.lastModified() > REFRESH_MS) {
            runCatching {
                val r = Http.get(URL)
                if (r.ok && parse(r.body).isNotEmpty()) file.writeText(r.body)
            }
        }
        return if (file.exists()) runCatching { parse(file.readText()) }.getOrDefault(emptyList()) else emptyList()
    }

    private fun parse(json: String): List<Photo> {
        val photos = JSONObject(json).optJSONArray("photos") ?: return emptyList()
        return (0 until photos.length()).mapNotNull { i ->
            val p = photos.optJSONObject(i) ?: return@mapNotNull null
            val id = p.optString("id")
            val path = p.optString("path")
            if (id.isEmpty() || path.isEmpty()) return@mapNotNull null
            val handle = p.optString("handle")
            Photo(
                id = id, width = 0, height = 0, likes = 0, description = null, color = null,
                rawUrl = "https://images.unsplash.com/$path",
                pageUrl = "https://unsplash.com/photos/$id?utm_source=oriel&utm_medium=referral",
                photographer = p.optString("by"),
                photographerUrl = "https://unsplash.com/@$handle?utm_source=oriel&utm_medium=referral",
                downloadLocation = null,
            )
        }
    }
}
