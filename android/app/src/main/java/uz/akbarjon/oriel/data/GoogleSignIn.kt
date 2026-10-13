package uz.akbarjon.oriel.data

import android.content.Context
import androidx.credentials.CredentialManager
import androidx.credentials.CustomCredential
import androidx.credentials.GetCredentialRequest
import androidx.credentials.exceptions.GetCredentialCancellationException
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential

/**
 * Google sign-in right on the phone (Credential Manager's account sheet), no browser. The Google ID token it gives is
 * exchanged for a Firebase session exactly like the website's hand-over (`Firebase.signInWithGoogle`).
 */
object GoogleSignIn {
    /**
     * The **Web** OAuth client of project oriel-gcp (Firebase console, Authentication, Google, Web SDK configuration).
     * The token's audience must be this client for Firebase to accept it. Empty: fall back to the website.
     */
    private const val WEB_CLIENT_ID = "739177186002-1ce701p8t3c0os20hlulp7eqdevv7v4a.apps.googleusercontent.com"

    val available get() = WEB_CLIENT_ID.isNotEmpty()

    /** The account sheet; the Google ID token, or null when the person closed it. [context] must be an Activity. */
    suspend fun idToken(context: Context): String? {
        val request = GetCredentialRequest.Builder()
            .addCredentialOption(GetSignInWithGoogleOption.Builder(WEB_CLIENT_ID).build())
            .build()
        val credential = try {
            CredentialManager.create(context).getCredential(context, request).credential
        } catch (_: GetCredentialCancellationException) {
            return null
        }
        check(credential is CustomCredential && credential.type == GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL) {
            "Unexpected credential ${credential.type}"
        }
        return GoogleIdTokenCredential.createFrom(credential.data).idToken
    }
}
