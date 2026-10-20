package uz.akbarjon.oriel.core

import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.FirebaseException
import uz.akbarjon.oriel.data.FirebaseFailure
import uz.akbarjon.oriel.data.Firebase
import uz.akbarjon.oriel.data.OrielException
import uz.akbarjon.oriel.data.Source

/**
 * The phone shows someone else's current wallpaper: the account's own computer (its `sync/current`) or a friend's,
 * followed with the code they shared (same as the desktop apps' Friends page). While following, the own schedule is
 * off. Checking costs one tiny Firestore read; a photo is downloaded only when it actually changed.
 */
class Following {
    private val store get() = Oriel.store

    /** Follows the friend whose code this is. Returns their first name. */
    suspend fun follow(input: String): String {
        val code = normalize(input) ?: throw OrielException("A code has 8 letters and numbers, like K7MQ-2XPA.")
        val tokens = Oriel.auth.tokens() ?: throw OrielException("Sign in to Oriel first.")
        try {
            val (uid, name, picture) = Firebase.getFollowCode(tokens, code) ?: throw OrielException("No one has that code. Check it and try again.")
            if (uid == tokens.uid) throw OrielException("That's your own code. Send it to a friend instead.")
            val me = Oriel.auth.account.value?.firstName?.ifBlank { null } ?: "A friend"
            Firebase.addFollower(tokens, uid, code, me)
            val friend = name.ifBlank { "your friend" }
            store.update { it.copy(source = Source.Friend, followUid = uid, followName = friend, followPicture = picture.ifBlank { null }) }
            store.updateState { it.copy(followedPhotoId = null, followProblem = null) }
            Scheduler.reschedule(Oriel.app)
            check()
            return friend
        } catch (e: FirebaseException) {
            throw OrielException(if (e.failure == FirebaseFailure.Network) "You seem to be offline. Try again." else "Couldn't follow that code. Try again.")
        }
    }

    /** Back to Oriel's own photos on the schedule. */
    suspend fun stop() {
        val s = store.settings.value
        if (s.source == Source.Friend && s.followUid != null) {
            try {
                Oriel.auth.tokens()?.let { Firebase.removeFollower(it, s.followUid) }
            } catch (_: FirebaseException) {
                // They may have stopped sharing already; leaving locally is what matters.
            }
        }
        store.update { it.copy(source = Source.Own, followUid = null, followName = null, followPicture = null) }
        store.updateState { it.copy(followProblem = null) }
        // Back on the schedule from its next tick, not with a change right now.
        Scheduler.restart(Oriel.app)
    }

    /**
     * Shows the photo the account's other devices (or the followed friend) set last, if it's new here. Every device of
     * an account shows the same wallpaper: a change anywhere (scheduled, Change now, Search, History) is published to
     * `sync/current`, and the others pick it up here. Locked (paused) only stops the schedule: a photo set by hand on
     * another device still shows here. Returns false only on a failure worth retrying.
     */
    suspend fun check(): Boolean {
        val s = store.settings.value
        val friend = s.source == Source.Friend
        val owner = (if (friend) s.followUid else Oriel.auth.account.value?.uid) ?: return true
        val current = try {
            // Offline, even getting a fresh token fails: handled below like any other failure, never a crash.
            val tokens = Oriel.auth.tokens() ?: return true
            Firebase.getCurrent(tokens, owner)
        } catch (e: FirebaseException) {
            if (e.failure == FirebaseFailure.Rejected && friend) {
                store.updateState { it.copy(followProblem = "${s.followName ?: "Your friend"} stopped sharing their wallpaper.") }
                return true
            }
            return e.failure != FirebaseFailure.Network
        }
        if (current == null) return true
        // Already handled, or it's the photo on this phone right now (this phone published it).
        if (current.photoId == store.state.value.followedPhotoId) return true
        if (current.photoId == Oriel.history.latest()?.photo?.id) {
            store.updateState { it.copy(followedPhotoId = current.photoId) }
            return true
        }

        // A friend's photo is shared on to this account's other devices; the account's own isn't sent back.
        Oriel.wallpapers.showPhase(Phase.Receiving)
        val result = Oriel.wallpapers.apply(current.toPhoto(), current.topic, publish = friend, track = false)
        Oriel.wallpapers.showPhase(null)
        return when (result) {
            is ChangeResult.Applied -> {
                store.updateState { it.copy(followedPhotoId = current.photoId, followProblem = null) }
                true
            }
            is ChangeResult.Failed -> !result.transient
            ChangeResult.Skipped -> true
        }
    }

    companion object {
        /** No 0/O, 1/I: codes get read aloud and typed by hand. */
        private const val ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"

        /** "k7mq-2xpa " becomes "K7MQ2XPA"; null when it can't be a code. */
        fun normalize(input: String): String? {
            val code = input.uppercase().filter { !it.isWhitespace() && it != '-' }
            return code.takeIf { it.length == 8 && it.all { c -> c in ALPHABET } }
        }
    }
}
