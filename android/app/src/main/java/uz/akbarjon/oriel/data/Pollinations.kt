package uz.akbarjon.oriel.data

import android.content.Context
import java.io.IOException
import java.time.LocalDate
import kotlin.math.max
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import uz.akbarjon.oriel.BuildConfig
import uz.akbarjon.oriel.Oriel

/**
 * AI wallpapers made by Pollinations from a few words, with the key built into the app (POLLINATIONS_KEY, like the
 * desktop apps). The words are first written out as a full scene by a small text model, then drawn in the phone's
 * shape. Pollinations keeps the picture at a public link (media.pollinations.ai/{id}), which syncs like an Unsplash
 * photo: the account's other devices download that link.
 */
object Pollinations {
    const val MEDIA = "https://media.pollinations.ai/"
    private const val IMAGES = "https://gen.pollinations.ai/v1/images/generations"
    private const val TEXT = "https://gen.pollinations.ai/v1/chat/completions"
    private const val MODEL = "zimage"
    private const val TEXT_MODEL = "openai/gpt-5.4-nano"
    /** Pictures this phone may make a day: the key is shared by everyone, so nobody uses it all up. */
    const val MAX_PER_DAY = 20

    private const val INSTRUCTIONS =
        "You write prompts for an AI image model that makes wallpapers for phones and computers. Turn the user's few words " +
            "into one vivid prompt of 60 to 90 words: the subject, the setting, the time of day and light, colors, mood, camera " +
            "or art style, and a wide 16:9 composition with the main subject near the center (a phone shows only the middle). " +
            "The scene is full-bleed: it runs to every edge of the image. Describe only the scene itself; never mention paper, canvas, frames, borders, edges, margins, posters, prints or screens. " +
            "Keep everything the user asked for, in " +
            "their language's meaning but written in English, and don't add people unless asked. No text, logos or " +
            "watermarks in the picture. Reply with the prompt only."

    val isConfigured get() = BuildConfig.POLLINATIONS_KEY.isNotBlank()
    private val auth get() = mapOf("Authorization" to "Bearer ${BuildConfig.POLLINATIONS_KEY}")

    private val prefs get() = Oriel.app.getSharedPreferences("created", Context.MODE_PRIVATE)

    /** Pictures this phone can still make today. */
    val leftToday: Int
        get() = max(0, MAX_PER_DAY - if (prefs.getString("day", null) == LocalDate.now().toString()) prefs.getInt("count", 0) else 0)

    private fun countOne() {
        val today = LocalDate.now().toString()
        val count = if (prefs.getString("day", null) == today) prefs.getInt("count", 0) else 0
        prefs.edit().putString("day", today).putInt("count", count + 1).apply()
    }

    /**
     * Paints a 16:9 picture for the words (2048 x 1152), and returns it as a photo whose link is the picture (not
     * downloaded yet). Wide on purpose: it syncs to the account's computers, where it fills the screen, and the phone
     * crops its own part (the middle, or where the user places it). [progress] hears "Imagining…" while the words are
     * written out, then "Painting…".
     */
    suspend fun create(prompt: String, style: String?, progress: (String) -> Unit = {}): Photo {
        if (!isConfigured) throw OrielException("This build of Oriel can't create AI wallpapers.")
        if (leftToday == 0) throw OrielException("You've created $MAX_PER_DAY wallpapers today, the most for one day. Come back tomorrow for more.")
        val words = if (style.isNullOrBlank()) prompt.trim() else "${prompt.trim()}, $style"
        progress("Imagining…")
        val described = expand(words) ?: words
        progress("Painting…")
        val (w, h) = WIDE
        val body = JSONObject()
            .put("model", MODEL).put("prompt", final(described, prompt.trim())).put("size", "${w}x$h")
            .put("seed", (1..Int.MAX_VALUE).random()).put("safe", true).put("response_format", "url")
        val response = try {
            Http.postSlow(IMAGES, body.toString(), auth)
        } catch (e: IOException) {
            throw OrielException("Couldn't reach Pollinations. Check your connection and try again.", transient = true)
        }
        if (!response.ok) throw OrielException(when (response.code) {
            401, 402, 403 -> "Creating is resting for now. Try again later."
            429 -> "That was a lot of pictures in a short time. Wait a minute and try again."
            400 -> "Pollinations couldn't make that. Try other words."
            else -> "Pollinations had a problem. Try again in a moment."
        }, transient = response.code >= 429)
        val link = try {
            JSONObject(response.body).getJSONArray("data").getJSONObject(0).getString("url")
        } catch (e: JSONException) {
            null
        }
        val photo = link?.let { fromLink(it, prompt.trim()) }
            ?: throw OrielException("Pollinations didn't send back a picture. Try again.", transient = true)
        countOne()
        return photo
    }

    /**
     * What the picture model gets: a full-bleed wallpaper of great quality, the written-out scene, and the user's own words
     * (so nothing they asked for is lost). Only what should be there: the model can't follow "no", and naming borders or
     * frames (even as "no border") is what made it draw white edges and rounded corners.
     */
    private fun final(scene: String, userWords: String) =
        "Full-bleed widescreen wallpaper, the scene runs to every edge of the image, great quality, highly detailed. " +
            "${scene.trimEnd('.')}. User's idea: ${userWords.trimEnd('.')}."

    /** An AI wallpaper's photo from its public link, or null when the link isn't one of Pollinations'. */
    fun fromLink(link: String, prompt: String?): Photo? {
        if (!link.startsWith(MEDIA)) return null
        val id = link.removePrefix(MEDIA).substringBefore('?').trim('/')
        if (id.length !in 16..128 || !id.all { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }) return null
        val clean = MEDIA + id
        return Photo(
            id = "ai-$id", width = 0, height = 0, likes = 0, description = prompt, color = null,
            rawUrl = clean, pageUrl = clean, photographer = "Pollinations AI", photographerUrl = "https://pollinations.ai",
            downloadLocation = null,
        )
    }

    /** The words written out as a full scene (60 to 90 words), or null when that didn't work. */
    private suspend fun expand(words: String): String? = try {
        val body = JSONObject().put("model", TEXT_MODEL).put("max_tokens", 400).put("messages", JSONArray()
            .put(JSONObject().put("role", "system").put("content", INSTRUCTIONS))
            .put(JSONObject().put("role", "user").put("content", words)))
        val response = Http.postSlow(TEXT, body.toString(), auth)
        if (!response.ok) null
        else JSONObject(response.body).getJSONArray("choices").getJSONObject(0).getJSONObject("message")
            .optString("content").trim().trim('"').takeIf { it.length > 20 }?.take(1200)
    } catch (e: IOException) {
        null
    } catch (e: JSONException) {
        null
    }

    /** 16:9 at the model's largest size (2560 wide is refused). */
    private val WIDE = 2048 to 1152
}
