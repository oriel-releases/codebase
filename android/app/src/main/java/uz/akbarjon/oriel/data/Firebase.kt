package uz.akbarjon.oriel.data

import java.io.IOException
import java.net.URLEncoder
import java.time.Instant
import org.json.JSONArray
import org.json.JSONObject

enum class FirebaseFailure { Network, Rejected, NotFound, Other }

class FirebaseException(val failure: FirebaseFailure, message: String) : Exception(message)

class FirebaseTokens(val idToken: String, val refreshToken: String, val expiresAt: Long, val uid: String)

/** Someone following this account: their account, first name and since when. */
data class Follower(val uid: String, val name: String, val since: Long)

class FirebaseProfile(val uid: String, val email: String, val displayName: String, val photoUrl: String, val tokens: FirebaseTokens)

/** `users/{uid}/sync/current`: the wallpaper an Oriel device applied last. */
class CurrentWallpaper(
    val photoId: String,
    val imageUrl: String,
    val pageUrl: String,
    val photographer: String,
    val photographerUrl: String,
    val description: String?,
    val topic: String?,
    val color: String?,
    val source: String,
) {
    /** The photo, rebuilt without asking Unsplash: the thumbnail URL minus its sizing is the raw URL. */
    fun toPhoto() = Photo(
        id = photoId, width = 0, height = 0, likes = 0, description = description, color = color,
        rawUrl = imageUrl.substringBefore('?'), pageUrl = pageUrl, photographer = photographer,
        photographerUrl = photographerUrl, downloadLocation = null,
    )
}

/**
 * Firebase Auth and Firestore over REST, exactly like the Windows app (`FirebaseClient.cs`): no Firebase SDK and no
 * Google Play services, so nothing runs in the background on Oriel's behalf. The key is the public web key; the data
 * is protected by Firebase Auth and firestore.rules.
 */
object Firebase {
    private const val API_KEY = "AIzaSyCtUNKnXo3UcKHsG_gtupyf1Tfpo-BzKc8"
    private const val DOCUMENTS = "projects/oriel-gcp/databases/(default)/documents"
    private const val DOCUMENTS_URL = "https://firestore.googleapis.com/v1/$DOCUMENTS"
    private const val IDENTITY = "https://identitytoolkit.googleapis.com/v1"
    /** The Realtime Database: only the doorbell `pulse/{uid}` lives there (see website/database.rules.json). */
    private const val REALTIME = "https://oriel-gcp-default-rtdb.firebaseio.com"

    /** The account's doorbell, readable and writable with its ID token (also as a live event stream: [uz.akbarjon.oriel.core.Pulse]). */
    fun pulseUrl(t: FirebaseTokens) = "$REALTIME/pulse/${t.uid}.json?auth=${URLEncoder.encode(t.idToken, "UTF-8")}"

    /**
     * Rings the account's doorbell after `sync/current` changed: the other devices listening fetch the new photo within
     * a second. [device] (this install's session id) lets this phone ignore its own ring.
     */
    suspend fun ring(t: FirebaseTokens, photoId: String, device: String) {
        val body = JSONObject().put("photoId", photoId).put("source", "android").put("device", device)
            .put("at", JSONObject().put(".sv", "timestamp"))
        // A PATCH: a photo ring leaves a settings ring's fields alone, and the other way round.
        val r = call { Http.patch(pulseUrl(t), body.toString()) }
        if (!r.ok) throw FirebaseException(FirebaseFailure.Other, "Realtime Database answered ${r.code}")
    }

    /** Rings the other devices: the shared settings changed (they read `sync/settings` again). */
    suspend fun ringSettings(t: FirebaseTokens, device: String) {
        val body = JSONObject().put("settingsAt", JSONObject().put(".sv", "timestamp")).put("settingsDevice", device)
        val r = call { Http.patch(pulseUrl(t), body.toString()) }
        if (!r.ok) throw FirebaseException(FirebaseFailure.Other, "Realtime Database answered ${r.code}")
    }

    // ---------------------------------------------------------------- favorites, the same on every device

    /** Writes one favorite (or un-favorite: the document stays with favorite = false, so the change syncs). */
    suspend fun setFavorite(t: FirebaseTokens, photo: Photo, favorite: Boolean, at: Long, device: String) {
        val fields = JSONObject()
            .put("photoId", str(photo.id)).put("imageUrl", str(photo.rawUrl.substringBefore('?')))
            .put("pageUrl", str(photo.pageUrl)).put("photographer", str(photo.photographer))
            .put("photographerUrl", str(photo.photographerUrl)).put("favorite", JSONObject().put("booleanValue", favorite))
            .put("updatedAt", time(Instant.ofEpochMilli(at))).put("device", str(device))
        photo.description?.takeIf { it.isNotBlank() }?.let { fields.put("description", str(it)) }
        commit(t, update("users/${t.uid}/favorites/${photo.id}", fields))
    }

    /** The favorites changed after [since] (epoch ms; all of them when null). */
    suspend fun getFavoritesSince(t: FirebaseTokens, since: Long?): List<uz.akbarjon.oriel.core.SyncedFavorite> {
        val query = JSONObject().put("from", JSONArray().put(JSONObject().put("collectionId", "favorites")))
        if (since != null) query.put("where", JSONObject().put("fieldFilter", JSONObject()
            .put("field", JSONObject().put("fieldPath", "updatedAt")).put("op", "GREATER_THAN")
            .put("value", JSONObject().put("timestampValue", Instant.ofEpochMilli(since).toString()))))
        val r = call { Http.post("$DOCUMENTS_URL/users/${t.uid}:runQuery", JSONObject().put("structuredQuery", query).toString(), headers = auth(t)) }
        if (!r.ok) throw FirebaseException(FirebaseFailure.Other, "Firestore answered ${r.code}")
        val rows = JSONArray(r.body)
        return (0 until rows.length()).mapNotNull { i ->
            val f = rows.optJSONObject(i)?.optJSONObject("document")?.optJSONObject("fields") ?: return@mapNotNull null
            val id = f.string("photoId") ?: return@mapNotNull null
            val image = f.string("imageUrl") ?: return@mapNotNull null
            val at = f.optJSONObject("updatedAt")?.optString("timestampValue")?.let { runCatching { Instant.parse(it).toEpochMilli() }.getOrNull() } ?: 0L
            val photo = Photo(id = id, width = 0, height = 0, likes = 0, description = f.string("description"), color = null,
                rawUrl = image, pageUrl = f.string("pageUrl") ?: "", photographer = f.string("photographer") ?: "",
                photographerUrl = f.string("photographerUrl") ?: "", downloadLocation = null)
            uz.akbarjon.oriel.core.SyncedFavorite(photo, f.optJSONObject("favorite")?.optBoolean("booleanValue") ?: false, at)
        }
    }

    /** Rings the other devices: favorites changed. */
    suspend fun ringFavorites(t: FirebaseTokens, device: String) {
        val body = JSONObject().put("favoritesAt", JSONObject().put(".sv", "timestamp")).put("favoritesDevice", device)
        val r = call { Http.patch(pulseUrl(t), body.toString()) }
        if (!r.ok) throw FirebaseException(FirebaseFailure.Other, "Realtime Database answered ${r.code}")
    }

    // ---------------------------------------------------------------- shared settings and the schedule's slot

    /** The account's shared settings and when they were written (epoch ms), or null when no device has shared any yet. */
    suspend fun getSyncedSettings(t: FirebaseTokens): Pair<uz.akbarjon.oriel.core.SyncedSettings, Long>? {
        val f = getDocument(t, "users/${t.uid}/sync/settings") ?: return null
        fun int(name: String, fallback: Int) = f.optJSONObject(name)?.optString("integerValue")?.toIntOrNull() ?: fallback
        fun bool(o: JSONObject, name: String) = o.optJSONObject(name)?.optBoolean("booleanValue") ?: false
        val values = f.optJSONObject("interests")?.optJSONObject("arrayValue")?.optJSONArray("values") ?: JSONArray()
        val interests = (0 until values.length()).mapNotNull { i ->
            val m = values.optJSONObject(i)?.optJSONObject("mapValue")?.optJSONObject("fields") ?: return@mapNotNull null
            val query = m.string("query") ?: return@mapNotNull null
            Interest(query, bool(m, "enabled"), bool(m, "isCustom"))
        }
        val settings = uz.akbarjon.oriel.core.SyncedSettings(
            f.string("dailyTime") ?: "09:00", f.string("timeZone") ?: "UTC", int("intervalMinutes", 0),
            bool(f, "paused"), bool(f, "favoritesOnly"), bool(f, "skipLowLikes"), int("minLikes", 50), interests,
            !f.has("matchTimeOfDay") || bool(f, "matchTimeOfDay"),
        )
        val at = f.optJSONObject("updatedAt")?.optString("timestampValue")?.let { runCatching { Instant.parse(it).toEpochMilli() }.getOrNull() } ?: 0L
        return settings to at
    }

    suspend fun setSyncedSettings(t: FirebaseTokens, s: uz.akbarjon.oriel.core.SyncedSettings, device: String, at: Long) {
        fun int(v: Int) = JSONObject().put("integerValue", v.toString())
        fun bool(v: Boolean) = JSONObject().put("booleanValue", v)
        val interests = JSONArray(s.interests.map {
            JSONObject().put("mapValue", JSONObject().put("fields", JSONObject()
                .put("query", str(it.query)).put("enabled", bool(it.enabled)).put("isCustom", bool(it.isCustom))))
        })
        commit(t, update("users/${t.uid}/sync/settings", JSONObject()
            .put("dailyTime", str(s.dailyTime)).put("timeZone", str(s.timeZone)).put("intervalMinutes", int(s.intervalMinutes))
            .put("paused", bool(s.paused)).put("favoritesOnly", bool(s.favoritesOnly)).put("skipLowLikes", bool(s.skipLowLikes))
            .put("minLikes", int(s.minLikes)).put("matchTimeOfDay", bool(s.matchTimeOfDay)).put("interests", JSONObject().put("arrayValue", JSONObject().put("values", interests)))
            .put("updatedAt", time(Instant.ofEpochMilli(at))).put("device", str(device))))
    }

    /**
     * Claims a scheduled moment for this phone ("first device wins"): a compare-and-set on one document, so of two
     * devices ticking together exactly one gets it. False when another device already holds this slot.
     */
    suspend fun claimSlot(t: FirebaseTokens, slotId: String, device: String): Boolean {
        val path = "users/${t.uid}/sync/slot"
        repeat(3) {
            val r = call { Http.get("$DOCUMENTS_URL/$path", auth(t)) }
            val precondition = if (r.code == 404) {
                JSONObject().put("exists", false)
            } else {
                val doc = ensureOk(r)
                if ((doc.optJSONObject("fields") ?: JSONObject()).string("slotId") == slotId) return false
                JSONObject().put("updateTime", doc.getString("updateTime"))
            }
            val write = update(path, JSONObject().put("slotId", str(slotId)).put("device", str(device)).put("at", time(Instant.now())))
                .put("currentDocument", precondition)
            try {
                commit(t, write)
                return true
            } catch (e: FirebaseException) {
                if (e.failure == FirebaseFailure.Network) throw e
                // Another device wrote it between the read and the write: look again.
            }
        }
        return false
    }

    /** Exchanges the Google ID token from the website for this app's own Firebase session. */
    suspend fun signInWithGoogle(googleIdToken: String): FirebaseProfile {
        val body = JSONObject()
            .put("postBody", "id_token=${URLEncoder.encode(googleIdToken, "UTF-8")}&providerId=google.com")
            .put("requestUri", "http://localhost")
            .put("returnSecureToken", true)
            .put("returnIdpCredential", true)
        val r = ensureOk(call { Http.post("https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=$API_KEY", body.toString()) })
        val uid = r.getString("localId")
        return FirebaseProfile(
            uid, r.optString("email"), r.optString("displayName"), r.optString("photoUrl"),
            tokens(r.getString("idToken"), r.getString("refreshToken"), r.optString("expiresIn"), uid),
        )
    }

    /** Email and password sign-in, made right here in the app: a session of its own. */
    suspend fun signInWithPassword(email: String, password: String): FirebaseProfile {
        val body = JSONObject().put("email", email).put("password", password).put("returnSecureToken", true)
        val r = ensureOk(call { Http.post("$IDENTITY/accounts:signInWithPassword?key=$API_KEY", body.toString()) })
        val t = tokens(r.getString("idToken"), r.getString("refreshToken"), r.optString("expiresIn"), r.getString("localId"))
        return profile(t)
    }

    /**
     * Creates an email and password account with its name, its `users/{uid}` document (as the website does) and a
     * verification email (best effort).
     */
    suspend fun createAccount(name: String, email: String, password: String): FirebaseProfile {
        val body = JSONObject().put("email", email).put("password", password).put("returnSecureToken", true)
        val r = ensureOk(call { Http.post("$IDENTITY/accounts:signUp?key=$API_KEY", body.toString()) })
        var t = tokens(r.getString("idToken"), r.getString("refreshToken"), r.optString("expiresIn"), r.getString("localId"))
        if (name.isNotBlank()) {
            val update = JSONObject().put("idToken", t.idToken).put("displayName", name.trim()).put("returnSecureToken", true)
            val u = ensureOk(call { Http.post("$IDENTITY/accounts:update?key=$API_KEY", update.toString()) })
            // The name goes into the token too, so fresh tokens carry it.
            if (u.has("idToken")) t = tokens(u.getString("idToken"), u.getString("refreshToken"), u.optString("expiresIn"), t.uid)
        }
        try {
            commit(t, JSONObject()
                .put("update", JSONObject().put("name", "$DOCUMENTS/users/${t.uid}").put("fields", JSONObject()
                    .put("email", str(email)).put("displayName", str(name.trim())).put("photoURL", str(""))))
                .put("updateTransforms", JSONArray().put(JSONObject().put("fieldPath", "createdAt").put("setToServerValue", "REQUEST_TIME")))
                .put("currentDocument", JSONObject().put("exists", false)))
        } catch (_: FirebaseException) {
            // The account exists already; failing here would only make "try again" say the email is taken.
        }
        try {
            val verify = JSONObject().put("requestType", "VERIFY_EMAIL").put("idToken", t.idToken)
            ensureOk(call { Http.post("$IDENTITY/accounts:sendOobCode?key=$API_KEY", verify.toString()) })
        } catch (_: FirebaseException) {
            // Only a nicety: they can ask again from the account page.
        }
        return FirebaseProfile(t.uid, email, name.trim(), "", t)
    }

    /** Sends a password reset link. Firebase doesn't say whether the address has an account, so neither does the app. */
    suspend fun sendPasswordReset(email: String) {
        val body = JSONObject().put("requestType", "PASSWORD_RESET").put("email", email)
        ensureOk(call { Http.post("$IDENTITY/accounts:sendOobCode?key=$API_KEY", body.toString()) })
    }

    /**
     * Takes over the Firebase session the website made for an account without Google (`oriel://auth?refresh=...`):
     * fresh tokens from its refresh token, then the profile, which the token exchange doesn't return.
     */
    suspend fun signInWithRefreshToken(refreshToken: String): FirebaseProfile = profile(refresh(refreshToken))

    private suspend fun profile(t: FirebaseTokens): FirebaseProfile {
        val body = JSONObject().put("idToken", t.idToken)
        val user = ensureOk(call { Http.post("$IDENTITY/accounts:lookup?key=$API_KEY", body.toString()) })
            .optJSONArray("users")?.optJSONObject(0)
            ?: throw FirebaseException(FirebaseFailure.Rejected, "Account not found.")
        return FirebaseProfile(t.uid, user.optString("email"), user.optString("displayName"), user.optString("photoUrl"), t)
    }

    suspend fun refresh(refreshToken: String): FirebaseTokens {
        val form = "grant_type=refresh_token&refresh_token=${URLEncoder.encode(refreshToken, "UTF-8")}"
        val r = ensureOk(call {
            Http.post("https://securetoken.googleapis.com/v1/token?key=$API_KEY", form, "application/x-www-form-urlencoded")
        })
        return tokens(r.getString("id_token"), r.getString("refresh_token"), r.optString("expires_in"), r.getString("user_id"))
    }

    // ---------------------------------------------------------------- this device's session

    suspend fun setSession(t: FirebaseTokens, sessionId: String, deviceName: String, appVersion: String, createdAt: Instant) =
        commit(t, update(sessionPath(t.uid, sessionId), JSONObject()
            .put("platform", str("android"))
            .put("deviceName", str(deviceName))
            .put("appVersion", str(appVersion))
            .put("createdAt", time(createdAt))
            .put("lastSeenAt", time(Instant.now()))))

    /** Bumps lastSeenAt. False when the session document is gone (signed out from somewhere else). */
    suspend fun touchSession(t: FirebaseTokens, sessionId: String, appVersion: String): Boolean = try {
        val fields = JSONObject().put("lastSeenAt", time(Instant.now())).put("appVersion", str(appVersion))
        commit(t, update(sessionPath(t.uid, sessionId), fields, mask = listOf("lastSeenAt", "appVersion"), mustExist = true))
        true
    } catch (e: FirebaseException) {
        if (e.failure == FirebaseFailure.NotFound) false else throw e
    }

    suspend fun deleteSession(t: FirebaseTokens, sessionId: String) =
        commit(t, JSONObject().put("delete", "$DOCUMENTS/${sessionPath(t.uid, sessionId)}"))

    // ---------------------------------------------------------------- the current wallpaper

    /** Publishes the wallpaper just applied; the latest write wins. */
    suspend fun setCurrent(t: FirebaseTokens, w: CurrentWallpaper) {
        val fields = JSONObject()
            .put("photoId", str(w.photoId))
            .put("imageUrl", str(w.imageUrl))
            .put("pageUrl", str(w.pageUrl))
            .put("photographer", str(w.photographer))
            .put("photographerUrl", str(w.photographerUrl))
            .put("source", str(w.source))
            .put("updatedAt", time(Instant.now()))
        w.description?.takeIf { it.isNotBlank() }?.let { fields.put("description", str(it)) }
        w.topic?.takeIf { it.isNotBlank() }?.let { fields.put("topic", str(it)) }
        w.color?.takeIf { it.isNotBlank() }?.let { fields.put("color", str(it)) }
        commit(t, update("users/${t.uid}/sync/current", fields))
    }

    /**
     * Someone's `sync/current` (yours, or a friend's you follow), or null if there is none yet. Throws
     * [FirebaseFailure.Rejected] once a friend stops sharing.
     */
    suspend fun getCurrent(t: FirebaseTokens, ownerUid: String): CurrentWallpaper? {
        val f = getDocument(t, "users/$ownerUid/sync/current") ?: return null
        val photoId = f.string("photoId") ?: return null
        return CurrentWallpaper(
            photoId, f.string("imageUrl") ?: return null, f.string("pageUrl") ?: "", f.string("photographer") ?: "",
            f.string("photographerUrl") ?: "", f.string("description"), f.string("topic"), f.string("color"), f.string("source") ?: "",
        )
    }

    /** Just the photo ID of someone's current wallpaper: the cheap check the background poll makes. */
    suspend fun getCurrentPhotoId(t: FirebaseTokens, ownerUid: String): String? =
        getDocument(t, "users/$ownerUid/sync/current?mask.fieldPaths=photoId")?.string("photoId")

    // ---------------------------------------------------------------- following a friend

    /** Who a follow code belongs to (uid, first name), or null when there is no such code. */
    /** Who a follow code belongs to: their account, first name and Google picture ("" when none), or null. */
    suspend fun getFollowCode(t: FirebaseTokens, code: String): Triple<String, String, String>? {
        val f = getDocument(t, "codes/$code") ?: return null
        return Triple(f.string("uid") ?: "", f.string("name") ?: "", f.string("picture") ?: "")
    }

    suspend fun addFollower(t: FirebaseTokens, friendUid: String, code: String, name: String) =
        commit(t, update("users/$friendUid/followers/${t.uid}", JSONObject()
            .put("code", str(code)).put("name", str(name)).put("createdAt", time(Instant.now()))))

    suspend fun removeFollower(t: FirebaseTokens, ownerUid: String) =
        commit(t, JSONObject().put("delete", "$DOCUMENTS/users/$ownerUid/followers/${t.uid}"))

    // ---------------------------------------------------------------- sharing your wallpaper (like the desktop apps)

    /** Claims a new follow code, with the first name and picture its page shows. Fails if the code is taken. */
    suspend fun createFollowCode(t: FirebaseTokens, code: String, name: String, picture: String) =
        commit(t, update("codes/$code", JSONObject().put("uid", str(t.uid)).put("name", str(name))
            .put("picture", str(picture)).put("createdAt", time(Instant.now())), exists = false))

    /** The wallpaper the follow page shows: your current one, updated as it changes. */
    suspend fun setFollowCodePhoto(t: FirebaseTokens, code: String, photoId: String, path: String, photographer: String, handle: String) =
        commit(t, update("codes/$code", JSONObject().put("photoId", str(photoId)).put("photoPath", str(path))
            .put("photographer", str(photographer)).put("photographerHandle", str(handle)),
            mask = listOf("photoId", "photoPath", "photographer", "photographerHandle"), mustExist = true))

    /** A shared wallpaper's `shares/{id}` document: the photo, and the sender's first name and picture. Fails if the id is taken. */
    suspend fun createShare(t: FirebaseTokens, id: String, from: String, picture: String, photoId: String, path: String,
                            photographer: String, handle: String) =
        commit(t, update("shares/$id", JSONObject().put("uid", str(t.uid)).put("from", str(from)).put("picture", str(picture))
            .put("photoId", str(photoId)).put("photoPath", str(path)).put("photographer", str(photographer))
            .put("photographerHandle", str(handle)).put("createdAt", time(Instant.now())), exists = false))

    suspend fun deleteFollowCode(t: FirebaseTokens, code: String) = commit(t, JSONObject().put("delete", "$DOCUMENTS/codes/$code"))

    /** The accounts following this one, newest first. */
    suspend fun listFollowers(t: FirebaseTokens): List<Follower> {
        val r = ensureOk(call { Http.get("$DOCUMENTS_URL/users/${t.uid}/followers?pageSize=300&mask.fieldPaths=name&mask.fieldPaths=createdAt", auth(t)) })
        val docs = r.optJSONArray("documents") ?: return emptyList()
        return (0 until docs.length()).map { docs.getJSONObject(it) }.map { d ->
            val f = d.optJSONObject("fields") ?: JSONObject()
            val since = f.optJSONObject("createdAt")?.optString("timestampValue")?.let { runCatching { Instant.parse(it).toEpochMilli() }.getOrNull() } ?: 0L
            Follower(d.getString("name").substringAfterLast('/'), f.string("name") ?: "A friend", since)
        }.sortedByDescending { it.since }
    }

    /** Stops someone following you (they can no longer read your `sync/current`). */
    suspend fun removeMyFollower(t: FirebaseTokens, followerUid: String) =
        commit(t, JSONObject().put("delete", "$DOCUMENTS/users/${t.uid}/followers/$followerUid"))

    // ---------------------------------------------------------------- plumbing

    private suspend fun getDocument(t: FirebaseTokens, path: String): JSONObject? {
        val response = call { Http.get("$DOCUMENTS_URL/$path", auth(t)) }
        if (response.code == 404) return null
        return ensureOk(response).optJSONObject("fields") ?: JSONObject()
    }

    private suspend fun commit(t: FirebaseTokens, vararg writes: JSONObject) {
        val body = JSONObject().put("writes", JSONArray(writes.toList()))
        ensureOk(call { Http.post("$DOCUMENTS_URL:commit", body.toString(), headers = auth(t)) })
    }

    private fun update(path: String, fields: JSONObject, mask: List<String>? = null, mustExist: Boolean = false, exists: Boolean? = null): JSONObject {
        val write = JSONObject().put("update", JSONObject().put("name", "$DOCUMENTS/$path").put("fields", fields))
        if (mask != null) write.put("updateMask", JSONObject().put("fieldPaths", JSONArray(mask)))
        val precondition = exists ?: if (mustExist) true else null
        if (precondition != null) write.put("currentDocument", JSONObject().put("exists", precondition))
        return write
    }

    private fun sessionPath(uid: String, sessionId: String) = "users/$uid/sessions/$sessionId"
    private fun auth(t: FirebaseTokens) = mapOf("Authorization" to "Bearer ${t.idToken}")
    private fun str(value: String) = JSONObject().put("stringValue", value)
    private fun time(value: Instant) = JSONObject().put("timestampValue", value.toString())
    private fun JSONObject.string(name: String): String? = optJSONObject(name)?.optString("stringValue")?.takeIf { it.isNotEmpty() }

    private fun tokens(idToken: String, refreshToken: String, expiresIn: String, uid: String) =
        FirebaseTokens(idToken, refreshToken, System.currentTimeMillis() + (expiresIn.toLongOrNull() ?: 3600) * 1000, uid)

    private suspend fun call(send: suspend () -> HttpResponse): HttpResponse = try {
        send()
    } catch (_: IOException) {
        throw FirebaseException(FirebaseFailure.Network, "Couldn't reach Google. Check your connection.")
    }

    private fun ensureOk(response: HttpResponse): JSONObject {
        if (response.ok) return if (response.body.isBlank()) JSONObject() else JSONObject(response.body)
        val message = try {
            val error = JSONObject(response.body).getJSONObject("error")
            "${error.optString("status")} ${error.optString("message")}".trim()
        } catch (_: Exception) {
            "Unexpected response from Google."
        }
        var failure = when (response.code) {
            404 -> FirebaseFailure.NotFound
            429 -> FirebaseFailure.Other
            in 400..499 -> FirebaseFailure.Rejected
            else -> FirebaseFailure.Other
        }
        // A conditional write on a missing document comes back as NOT_FOUND or FAILED_PRECONDITION.
        if ("NOT_FOUND" in message || "FAILED_PRECONDITION" in message) failure = FirebaseFailure.NotFound
        throw FirebaseException(failure, message)
    }
}
