package uz.akbarjon.oriel.core

import java.io.File
import java.time.Instant
import org.json.JSONObject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Http
import uz.akbarjon.oriel.data.Photo
import uz.akbarjon.oriel.data.Unsplash

/** This hour's Explore photos for phones, and the interest each was found under. */
class ExploreFeed(val hour: Instant, val photos: List<Photo>, val topics: Map<String, String>) {
    companion object {
        val Empty = ExploreFeed(Instant.EPOCH, emptyList(), emptyMap())
    }
}

/**
 * The Explore tab's photos: about 150 portrait wallpapers picked once an hour for every Oriel user by the public
 * oriel-releases/explore repo (metadata only, in Unsplash's own shape), so scrolling costs no Unsplash requests at all.
 * Kept on the phone (`explore.json`) and fetched again once a newer hour is due, like the desktop apps' ExploreService.
 */
object Explore {
    private const val URL = "https://raw.githubusercontent.com/oriel-releases/explore/feed/explore.json"
    /** The hour's feed is published a few minutes past the hour (sometimes later): until then, ask again this often. */
    private const val RETRY_MS = 5 * 60_000L
    private const val HOUR_MS = 3_600_000L

    private val lock = Mutex()
    private var feed: ExploreFeed? = null
    private var triedAt = 0L

    /** This hour's photos; the last ones kept when the new ones can't be had; empty when there never were any. */
    suspend fun feed(): ExploreFeed = lock.withLock {
        withContext(Dispatchers.IO) {
            val file = File(Oriel.app.filesDir, "explore.json")
            if (feed == null && file.exists()) feed = runCatching { parse(file.readText()) }.getOrNull()
            val now = System.currentTimeMillis()
            if (!isCurrent(feed, now) && now - triedAt >= RETRY_MS) {
                triedAt = now
                runCatching {
                    val r = Http.get(URL)
                    val fresh = if (r.ok) parse(r.body) else null
                    if (fresh != null && fresh.photos.isNotEmpty() && fresh.hour >= (feed?.hour ?: Instant.EPOCH)) {
                        feed = fresh
                        file.writeText(r.body)
                    }
                }
            }
            feed ?: ExploreFeed.Empty
        }
    }

    /** The feed is for this hour (UTC) or later: nothing newer to fetch yet. */
    private fun isCurrent(feed: ExploreFeed?, now: Long) =
        feed != null && feed.photos.isNotEmpty() && feed.hour.toEpochMilli() >= now / HOUR_MS * HOUR_MS

    /** The phones' half of the feed (`portrait`); photos it can't read are left out. */
    private fun parse(json: String): ExploreFeed {
        val root = JSONObject(json)
        val hour = runCatching { Instant.parse(root.getString("hour")) }.getOrDefault(Instant.EPOCH)
        val list = root.optJSONArray("portrait") ?: return ExploreFeed.Empty
        val topics = HashMap<String, String>()
        val photos = (0 until list.length()).mapNotNull { i ->
            val p = list.optJSONObject(i) ?: return@mapNotNull null
            val photo = runCatching { Unsplash.parse(p) }.getOrNull() ?: return@mapNotNull null
            if (!photo.rawUrl.startsWith("https://images.unsplash.com/")) return@mapNotNull null
            p.optString("topic").takeIf { it.isNotBlank() }?.let { topics[photo.id] = it }
            photo
        }.distinctBy { it.id }
        return ExploreFeed(hour, photos, topics)
    }
}
