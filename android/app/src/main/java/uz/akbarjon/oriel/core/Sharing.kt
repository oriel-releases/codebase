package uz.akbarjon.oriel.core

import android.net.Uri
import android.util.Log
import kotlin.random.Random
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Firebase
import uz.akbarjon.oriel.data.FirebaseException
import uz.akbarjon.oriel.data.FirebaseFailure
import uz.akbarjon.oriel.data.Follower
import uz.akbarjon.oriel.data.OrielException
import uz.akbarjon.oriel.data.Photo

/**
 * Sharing your wallpaper with friends, like the desktop apps' Friends page: a follow code (`codes/{code}`) friends type
 * in or open as a link (`/follow/{code}`), the people following you, and turning it off. The code's page shows your
 * current wallpaper, updated after every change ([showOnFollowPage]).
 */
object Sharing {
    private const val TAG = "OrielSharing"
    private const val ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"

    /** "K7MQ2XPA" shown as "K7MQ-2XPA". */
    fun format(code: String) = if (code.length == 8) "${code.take(4)}-${code.drop(4)}" else code

    fun link(code: String) = "https://oriel.akbarjon.uz/follow/$code"

    /** Starts sharing: a new code friends can follow (the same one after that). */
    suspend fun start(): String {
        Oriel.store.settings.value.myFollowCode?.let { return it }
        val tokens = Oriel.auth.tokens() ?: throw OrielException("Sign in to Oriel first.")
        val account = Oriel.auth.account.value
        val name = account?.firstName?.ifBlank { null } ?: "A friend"
        val picture = account?.photoUrl?.takeIf { it.startsWith("https://lh3.googleusercontent.com/") && it.length <= 400 } ?: ""
        repeat(4) { attempt ->
            val code = (1..8).map { ALPHABET[Random.nextInt(ALPHABET.length)] }.joinToString("")
            try {
                Firebase.createFollowCode(tokens, code, name, picture)
                Oriel.store.update { it.copy(myFollowCode = code) }
                Oriel.history.latest()?.photo?.let { showOnFollowPage(it) }
                return code
            } catch (e: FirebaseException) {
                if (e.failure == FirebaseFailure.Network) throw OrielException("You seem to be offline. Try again.")
                Log.i(TAG, "Code taken or refused (attempt ${attempt + 1}): ${e.message}")
            }
        }
        throw OrielException("Couldn't create a code. Try again.")
    }

    /** Stops sharing: the code stops working and everyone following stops seeing your wallpaper. */
    suspend fun stop() {
        val tokens = Oriel.auth.tokens() ?: throw OrielException("Sign in to Oriel first.")
        try {
            Oriel.store.settings.value.myFollowCode?.let { Firebase.deleteFollowCode(tokens, it) }
            Firebase.listFollowers(tokens).forEach { Firebase.removeMyFollower(tokens, it.uid) }
        } catch (e: FirebaseException) {
            throw OrielException(if (e.failure == FirebaseFailure.Network) "You seem to be offline. Try again." else "Couldn't stop sharing. Try again.")
        }
        Oriel.store.update { it.copy(myFollowCode = null) }
    }

    /** The people following you, newest first; null when it can't be read right now. */
    suspend fun followers(): List<Follower>? = try {
        Oriel.auth.tokens()?.let { Firebase.listFollowers(it) }
    } catch (_: FirebaseException) {
        null
    }

    suspend fun removeFollower(uid: String) {
        val tokens = Oriel.auth.tokens() ?: return
        try {
            Firebase.removeMyFollower(tokens, uid)
        } catch (e: FirebaseException) {
            throw OrielException(if (e.failure == FirebaseFailure.Network) "You seem to be offline. Try again." else "Couldn't remove them. Try again.")
        }
    }

    /** The photo's image path on images.unsplash.com, as the website's pages need it; null for photos that can't be shared. */
    private fun pathOf(photo: Photo): String? =
        Uri.parse(photo.rawUrl).takeIf { it.host == "images.unsplash.com" }?.path?.trimStart('/')
            ?.takeIf { Regex("[A-Za-z0-9_-]{6,120}").matches(it) }

    private fun handleOf(photo: Photo): String = Uri.parse(photo.photographerUrl).path?.trim('/')?.removePrefix("@")
        ?.takeIf { Regex("[A-Za-z0-9_.-]{0,40}").matches(it) }.orEmpty()

    private val links = mutableMapOf<String, String>()

    /**
     * A link to this wallpaper (`oriel.akbarjon.uz/share/AB3K9QXZ`), like the desktop apps' Share button: whoever
     * opens it can set it in one click. Made once per photo while the app runs.
     */
    suspend fun shareLink(photo: Photo): String {
        links[photo.id]?.let { return it }
        val path = pathOf(photo) ?: throw OrielException("Only photos from Unsplash can be shared.")
        val tokens = Oriel.auth.tokens() ?: throw OrielException("Sign in to Oriel first.")
        val account = Oriel.auth.account.value
        val from = account?.firstName.orEmpty().filter { !it.isISOControl() && it != '<' && it != '>' }.take(30)
        val picture = account?.photoUrl?.takeIf { it.startsWith("https://lh3.googleusercontent.com/") && it.length <= 400 } ?: ""
        repeat(4) {
            val id = (1..8).map { ALPHABET[Random.nextInt(ALPHABET.length)] }.joinToString("")
            try {
                Firebase.createShare(tokens, id, from, picture, photo.id, path, photo.photographer.take(80), handleOf(photo))
                return "https://oriel.akbarjon.uz/share/$id".also { links[photo.id] = it }
            } catch (e: FirebaseException) {
                if (e.failure == FirebaseFailure.Network) throw OrielException("You seem to be offline. Try again.")
            }
        }
        throw OrielException("Couldn't make a link. Try again.")
    }

    /** Puts this photo on your follow page. Best effort: the page just shows an older one if it fails. */
    suspend fun showOnFollowPage(photo: Photo) {
        val code = Oriel.store.settings.value.myFollowCode ?: return
        val tokens = Oriel.auth.tokens() ?: return
        val path = pathOf(photo) ?: return
        try {
            Firebase.setFollowCodePhoto(tokens, code, photo.id, path, photo.photographer.take(80), handleOf(photo))
        } catch (e: Exception) {
            Log.i(TAG, "Couldn't put the wallpaper on the follow page: ${e.message}")
        }
    }
}
