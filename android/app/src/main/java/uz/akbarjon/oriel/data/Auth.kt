package uz.akbarjon.oriel.data

import android.content.Context
import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import java.time.Instant
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import uz.akbarjon.oriel.BuildConfig

/** The signed-in account (Google or email and password), as kept on this phone. */
data class Account(
    val uid: String,
    val email: String,
    val displayName: String,
    val photoUrl: String,
    val refreshToken: String,
    /** This install's document in `users/{uid}/sessions`. */
    val sessionId: String,
) {
    val firstName get() = displayName.trim().substringBefore(' ').take(30)
}

/**
 * Sign-in, like the desktop apps: the website signs in with Google and hands over a Google ID token
 * (`oriel://auth?token=...`, or `?refresh=` for an email account), which becomes this app's own Firebase session plus a
 * session document. Email and password can also be typed right in the app. After that the
 * app stays signed in, offline too. It signs out only when Google rejects the account or the session document was
 * deleted (signed out from the account page).
 */
class Auth(context: Context) {
    private val prefs = context.getSharedPreferences("account", Context.MODE_PRIVATE)
    private val mutex = Mutex()
    private var tokens: FirebaseTokens? = null

    private val _account = MutableStateFlow(load())
    val account: StateFlow<Account?> = _account.asStateFlow()

    val isSignedIn get() = _account.value != null

    suspend fun signIn(googleIdToken: String): Account = signIn { Firebase.signInWithGoogle(googleIdToken) }

    /** The website's hand-over for an account without Google (`oriel://auth?refresh=...`). */
    suspend fun signInWithSession(refreshToken: String): Account = signIn { Firebase.signInWithRefreshToken(refreshToken) }

    suspend fun signInWithPassword(email: String, password: String): Account =
        signIn { Firebase.signInWithPassword(email.trim(), password) }

    suspend fun createAccount(name: String, email: String, password: String): Account =
        signIn { Firebase.createAccount(name, email.trim(), password) }

    suspend fun sendPasswordReset(email: String) = Firebase.sendPasswordReset(email.trim())

    private suspend fun signIn(start: suspend () -> FirebaseProfile): Account = mutex.withLock {
        val profile = start()
        val account = Account(profile.uid, profile.email, profile.displayName, profile.photoUrl, profile.tokens.refreshToken,
            UUID.randomUUID().toString().replace("-", ""))
        // Record this install first: if that fails nothing is saved and the person can just try again.
        Firebase.setSession(profile.tokens, account.sessionId, deviceName(), BuildConfig.VERSION_NAME, Instant.now())
        save(account)
        tokens = profile.tokens
        _account.value = account
        account
    }

    /** Fresh Firebase credentials, or null when signed out. */
    suspend fun tokens(): FirebaseTokens? = mutex.withLock {
        val account = _account.value ?: return null
        ensureTokens(account)
    }

    suspend fun signOut() = mutex.withLock {
        val account = _account.value ?: return@withLock
        try {
            // Never longer than a few seconds: on a bad connection, signing out here is what matters.
            kotlinx.coroutines.withTimeoutOrNull(8_000) { Firebase.deleteSession(ensureTokens(account), account.sessionId) }
        } catch (_: FirebaseException) {
            // Offline or already revoked: signing out here is what matters.
        }
        forget()
    }

    /** Refreshes the token and records activity. Returns false if this sign-in was revoked (and signs out). */
    suspend fun validate(): Boolean = mutex.withLock {
        val account = _account.value ?: return false
        try {
            if (Firebase.touchSession(ensureTokens(account), account.sessionId, BuildConfig.VERSION_NAME)) return true
            forget()
            false
        } catch (e: FirebaseException) {
            if (e.failure == FirebaseFailure.Rejected) {
                forget(); false
            } else true // offline or Google is down: stay signed in
        }
    }

    private suspend fun ensureTokens(account: Account): FirebaseTokens {
        tokens?.let { if (it.expiresAt > System.currentTimeMillis() + 120_000) return it }
        val fresh = Firebase.refresh(account.refreshToken)
        if (fresh.refreshToken != account.refreshToken) {
            val updated = account.copy(refreshToken = fresh.refreshToken)
            save(updated)
            _account.value = updated
        }
        tokens = fresh
        return fresh
    }

    private fun forget() {
        prefs.edit().clear().apply()
        tokens = null
        _account.value = null
    }

    private fun load(): Account? {
        val uid = prefs.getString("uid", null) ?: return null
        val refresh = prefs.getString("refresh", null)?.let { Secret.decrypt(it) } ?: return null
        return Account(uid, prefs.getString("email", "")!!, prefs.getString("name", "")!!, prefs.getString("photo", "")!!,
            refresh, prefs.getString("session", "")!!)
    }

    private fun save(a: Account) {
        prefs.edit()
            .putString("uid", a.uid).putString("email", a.email).putString("name", a.displayName).putString("photo", a.photoUrl)
            .putString("refresh", Secret.encrypt(a.refreshToken)).putString("session", a.sessionId)
            .apply()
    }

    private fun deviceName(): String {
        val maker = Build.MANUFACTURER.replaceFirstChar { it.uppercase() }
        return if (Build.MODEL.startsWith(Build.MANUFACTURER, ignoreCase = true)) Build.MODEL else "$maker ${Build.MODEL}"
    }
}

/** Encrypts the refresh token with a key that never leaves the Android Keystore. */
private object Secret {
    private const val ALIAS = "oriel-account"

    fun encrypt(plain: String): String {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, key()) }
        val sealed = cipher.iv + cipher.doFinal(plain.toByteArray())
        return Base64.encodeToString(sealed, Base64.NO_WRAP)
    }

    /** Null when it can't be read any more (key gone after a restore): the person just signs in again. */
    fun decrypt(encoded: String): String? = try {
        val sealed = Base64.decode(encoded, Base64.NO_WRAP)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, sealed, 0, 12))
        String(cipher.doFinal(sealed, 12, sealed.size - 12))
    } catch (_: Exception) {
        null
    }

    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getEntry(ALIAS, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build())
        }.generateKey()
    }
}
