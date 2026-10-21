package uz.akbarjon.oriel.data

import java.io.IOException
import java.net.URLEncoder
import org.json.JSONObject
import uz.akbarjon.oriel.BuildConfig

/** A failure worth showing to the user; [transient] ones are retried later on their own. */
class OrielException(message: String, val transient: Boolean = false) : Exception(message)

/** One page of search results, most relevant first. */
class SearchPage(val photos: List<Photo>, val totalPages: Int)

/**
 * The Unsplash API, with the key every Oriel install shares (50 requests an hour on a demo key). A search costs one
 * request and so does the download ping; the images themselves come from the CDN and cost nothing.
 */
object Unsplash {
    private const val API = "https://api.unsplash.com/"
    private const val UTM = "utm_source=oriel&utm_medium=referral"

    /** Requests left this hour, once Unsplash has told us. */
    @Volatile var remaining: Int? = null
        private set
    @Volatile private var limitedUntil = 0L

    val isConfigured get() = BuildConfig.UNSPLASH_ACCESS_KEY.isNotBlank()

    suspend fun search(query: String, page: Int = 1, orientation: String = "portrait"): SearchPage {
        val url = "${API}search/photos?orientation=$orientation&content_filter=high&order_by=relevant&per_page=30" +
            "&page=${page.coerceAtLeast(1)}&query=${URLEncoder.encode(query, "UTF-8")}"
        val json = JSONObject(api(url).body)
        val results = json.getJSONArray("results")
        return SearchPage((0 until results.length()).map { parse(results.getJSONObject(it)) }, json.optInt("total_pages", 1))
    }

    suspend fun photo(id: String): Photo = parse(JSONObject(api("${API}photos/${URLEncoder.encode(id, "UTF-8")}").body))

    /** Tells Unsplash the photo was used, as its guidelines require. Never fails the change. */
    suspend fun trackDownload(photo: Photo) {
        val url = photo.downloadLocation ?: return
        try {
            api(url)
        } catch (_: Exception) {
        }
    }

    private suspend fun api(url: String): HttpResponse {
        if (!isConfigured) throw OrielException("This build of Oriel has no Unsplash access key.")
        if (System.currentTimeMillis() < limitedUntil) throw rateLimited()
        val response = try {
            Http.get(url, mapOf("Authorization" to "Client-ID ${BuildConfig.UNSPLASH_ACCESS_KEY}", "Accept-Version" to "v1"))
        } catch (_: IOException) {
            throw OrielException("Can't reach Unsplash. Are you offline?", transient = true)
        }
        response.header("X-Ratelimit-Remaining")?.toIntOrNull()?.let {
            remaining = it
            if (it <= 0) limitedUntil = nextHour()
        }
        if (response.ok) return response
        throw when (response.code) {
            401 -> OrielException("Unsplash rejected Oriel's access key.")
            403, 429 -> if (response.code == 429 || remaining == 0) {
                limitedUntil = nextHour(); rateLimited()
            } else OrielException("Unsplash refused the request.")
            404 -> OrielException("That photo isn't available any more.")
            else -> OrielException("Unsplash returned an error (${response.code}).", transient = true)
        }
    }

    private fun rateLimited() = OrielException("Unsplash's hourly limit was reached. Oriel will try again later.", transient = true)

    /** Limits reset on the hour. */
    private fun nextHour(): Long {
        val hour = 3_600_000L
        return (System.currentTimeMillis() / hour + 1) * hour
    }

    /** One photo in Unsplash's shape: from the API, or from the Explore feed (which keeps the same fields). */
    internal fun parse(p: JSONObject): Photo {
        val user = p.getJSONObject("user")
        val urls = p.getJSONObject("urls")
        val links = p.getJSONObject("links")
        fun JSONObject.str(name: String) = optString(name).takeIf { has(name) && !isNull(name) && it.isNotBlank() }
        val description = listOfNotNull(p.str("alt_description"), p.str("description"))
            .distinctBy { it.lowercase() }.joinToString(" — ").ifBlank { null }
        return Photo(
            id = p.getString("id"),
            width = p.getInt("width"),
            height = p.getInt("height"),
            likes = p.optInt("likes"),
            description = description,
            color = p.str("color"),
            rawUrl = urls.getString("raw"),
            pageUrl = addUtm(links.getString("html")),
            photographer = user.str("name") ?: user.str("username") ?: "Unknown",
            photographerUrl = addUtm(user.getJSONObject("links").getString("html")),
            downloadLocation = links.str("download_location"),
        )
    }

    private fun addUtm(url: String) = url + (if ('?' in url) '&' else '?') + UTM
}
