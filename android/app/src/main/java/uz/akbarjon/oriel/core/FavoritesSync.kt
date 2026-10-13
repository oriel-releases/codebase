package uz.akbarjon.oriel.core

import android.util.Log
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Firebase
import uz.akbarjon.oriel.data.FirebaseException
import uz.akbarjon.oriel.data.Photo

/** One favorite as the account keeps it: the photo, and whether it's (still) a favorite. */
data class SyncedFavorite(val photo: Photo, val favorite: Boolean, val updatedAt: Long)

/**
 * The same favorites on every device, like the desktop apps' FavoritesSync: starring or un-starring here writes
 * `users/{uid}/favorites/{photoId}` and rings the other devices; a ring ([Pulse]), opening the app or the background
 * job brings the changes made elsewhere (only those since the last sync). A favorite of a photo this phone never
 * showed is added to History. The first sync uploads the favorites the phone already had.
 */
object FavoritesSync {
    private const val TAG = "OrielFavorites"
    private val gate = Mutex()

    /** Stars or un-stars a photo here and on every device. Use this, not History.setFavorite, from the screens. */
    fun set(photoId: String, favorite: Boolean) {
        Oriel.scope.launch {
            Oriel.history.setFavorite(photoId, favorite)
            val entry = Oriel.history.get(photoId) ?: return@launch
            push(entry.photo, favorite, ring = true)
        }
    }

    private suspend fun push(photo: Photo, favorite: Boolean, ring: Boolean) {
        if (!photo.rawUrl.startsWith("https://images.unsplash.com/")) return
        try {
            val tokens = Oriel.auth.tokens() ?: return
            val device = Oriel.auth.account.value?.sessionId.orEmpty()
            Firebase.setFavorite(tokens, photo, favorite, System.currentTimeMillis(), device)
            if (ring) Firebase.ringFavorites(tokens, device)
        } catch (e: FirebaseException) {
            Log.i(TAG, "Couldn't share the favorite ${photo.id}: ${e.message}")
        }
    }

    /** Brings here the favorites changed on other devices since the last sync. */
    suspend fun pull() {
        if (!gate.tryLock()) return
        try {
            val tokens = Oriel.auth.tokens() ?: return
            val since = Oriel.store.state.value.favoritesSyncedAt.takeIf { it > 0 }
            val changes = try {
                Firebase.getFavoritesSince(tokens, since)
            } catch (e: FirebaseException) {
                Log.i(TAG, "Couldn't read the shared favorites: ${e.message}")
                return
            }
            for (change in changes.sortedBy { it.updatedAt }) {
                val mine = Oriel.history.get(change.photo.id)
                when {
                    mine != null -> if (mine.isFavorite != change.favorite) Oriel.history.setFavorite(change.photo.id, change.favorite)
                    change.favorite -> {
                        // A photo never shown here: into History, filed at its own time (it doesn't become the current one).
                        val newest = Oriel.history.latest()?.appliedAt
                        val at = if (newest != null && change.updatedAt >= newest) newest - 1_000 else change.updatedAt
                        Oriel.history.add(change.photo, "favorite", at)
                        Oriel.history.setFavorite(change.photo.id, true)
                    }
                }
            }
            if (since == null) {
                // First sync here: the account gets this phone's favorites too.
                val known = changes.map { it.photo.id }.toSet()
                Oriel.history.list(favoritesOnly = true).filter { it.photo.id !in known }.forEach { push(it.photo, true, ring = false) }
                runCatching { Firebase.ringFavorites(tokens, Oriel.auth.account.value?.sessionId.orEmpty()) }
            }
            val newest = changes.maxOfOrNull { it.updatedAt } ?: since ?: 1L
            Oriel.store.updateState { it.copy(favoritesSyncedAt = newest) }
            if (changes.isNotEmpty()) Log.i(TAG, "Took ${changes.size} favorite change(s) from your other devices")
        } finally {
            gate.unlock()
        }
    }
}
