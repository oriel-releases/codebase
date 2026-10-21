package uz.akbarjon.oriel.ui

import androidx.compose.foundation.layout.width
import android.content.Context
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.autofill.ContentType
import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.contentType
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.credentials.exceptions.NoCredentialException
import kotlinx.coroutines.launch
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.R
import uz.akbarjon.oriel.core.Scheduler
import uz.akbarjon.oriel.data.FirebaseException
import uz.akbarjon.oriel.data.FirebaseFailure
import uz.akbarjon.oriel.data.GoogleSignIn

/** Progress of the hand-over from the website (`oriel://auth?token=...`), shown on the sign-in screen. */
object SignInState {
    var working by mutableStateOf(false)
    var error by mutableStateOf<String?>(null)
}

const val LOGIN_URL = "https://oriel.akbarjon.uz/login"

/** New passwords: Firebase's own minimum is 6; 8 is the floor worth having (the website asks the same). */
private const val MIN_PASSWORD = 8

private enum class EmailMode { SignIn, SignUp, Reset }

/** After any sign-in: mark it checked, then start the schedule from now. */
internal fun afterSignIn() {
    // The first sign-in on this phone (it never set a wallpaper): one right away, like on the computers. The
    // account's own photo when it has one, else a new pick from its interests (pulled first).
    val first = Oriel.store.state.value.lastAppliedAt == 0L
    Oriel.store.updateState { it.copy(lastValidatedAt = System.currentTimeMillis()) }
    Oriel.scope.launch {
        uz.akbarjon.oriel.core.SettingsSync.pull(); uz.akbarjon.oriel.core.FavoritesSync.pull()
        if (first) {
            Oriel.following.check()
            // The tour ends with the first wallpaper; when it was done before (a reinstall), one comes now.
            val state = Oriel.store.state.value
            if (state.onboardingDone && state.lastAppliedAt == 0L) Oriel.wallpapers.changeNow()
        }
    }
    Scheduler.restart(Oriel.app)
}

/**
 * Continue with Google: the phone's own account sheet, or the website when that isn't set up (no client ID yet) or the
 * phone has no Google account to offer.
 */
private suspend fun continueWithGoogle(context: Context) {
    SignInState.error = null
    if (!GoogleSignIn.available) return openUrl(context, LOGIN_URL)
    val token = try {
        GoogleSignIn.idToken(context) ?: return
    } catch (_: NoCredentialException) {
        return openUrl(context, LOGIN_URL)
    } catch (_: Exception) {
        SignInState.error = "Couldn't reach Google. Try again."
        return
    }
    SignInState.working = true
    // The app's own scope: this screen goes away as soon as the account is in, and the first wallpaper must still come.
    Oriel.scope.launch {
        try {
            Oriel.auth.signIn(token)
            afterSignIn()
        } catch (e: FirebaseException) {
            SignInState.error = if (e.failure == FirebaseFailure.Network) "You seem to be offline. Try again."
            else "Couldn't sign you in. Please try again."
        } catch (_: Exception) {
            SignInState.error = "Couldn't sign you in. Please try again."
        } finally {
            SignInState.working = false
        }
    }
}

@Composable
fun SignInScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    // Null: just the two buttons. Otherwise the email form, on that screen.
    var email by rememberSaveable { mutableStateOf<EmailMode?>(null) }
    BackHandler(enabled = email != null) { email = null }

    Column(
        Modifier.fillMaxSize().safeDrawingPadding().imePadding().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Spacer(Modifier.weight(1f))
        Logo(36.dp)
        Spacer(Modifier.height(20.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Oriel", style = MaterialTheme.typography.displaySmall)
            Spacer(Modifier.width(10.dp))
            BetaBadge()
        }
        Spacer(Modifier.height(10.dp))
        Text("A new wallpaper, every day.", style = MaterialTheme.typography.bodyLarge, color = Palette.colors.muted, textAlign = TextAlign.Center)
        Spacer(Modifier.weight(1f))
        Spacer(Modifier.height(40.dp))

        Column(Modifier.widthIn(max = 420.dp).fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
            PrimaryButton(
                if (SignInState.working) "Signing in…" else "Continue with Google",
                onClick = { scope.launch { continueWithGoogle(context) } },
                modifier = Modifier.fillMaxWidth(), busy = SignInState.working,
                icon = { Image(painterResource(R.drawable.ic_google), contentDescription = null, modifier = Modifier.size(18.dp)) },
            )
            SignInState.error?.let {
                Spacer(Modifier.height(16.dp))
                Text(it, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error, textAlign = TextAlign.Center)
            }
            Spacer(Modifier.height(10.dp))

            AnimatedContent(email, transitionSpec = { fadeIn() togetherWith fadeOut() }, label = "email") { mode ->
                if (mode == null) {
                    SecondaryButton("Continue with email", onClick = { email = EmailMode.SignIn }, modifier = Modifier.fillMaxWidth(),
                        enabled = !SignInState.working,
                        icon = { Icon(OrielIcons.Mail, contentDescription = null, modifier = Modifier.size(18.dp)) })
                } else {
                    EmailForm(mode, onMode = { email = it })
                }
            }
        }
    }
}

@Composable
private fun OrDivider() {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.weight(1f).height(0.5.dp).background(Palette.colors.line))
        Text("or", Modifier.padding(horizontal = 16.dp), style = MaterialTheme.typography.bodySmall, color = Palette.colors.muted)
        Box(Modifier.weight(1f).height(0.5.dp).background(Palette.colors.line))
    }
}

/** Sign in, create an account, or get a reset link, right in the app. */
@Composable
private fun EmailForm(mode: EmailMode, onMode: (EmailMode) -> Unit) {
    val scope = rememberCoroutineScope()
    val focus = LocalFocusManager.current
    var name by rememberSaveable { mutableStateOf("") }
    var address by rememberSaveable { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var shown by remember { mutableStateOf(false) }
    var working by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    var resetSent by remember { mutableStateOf(false) }
    val busy = working || SignInState.working

    fun switchTo(next: EmailMode) {
        error = null
        resetSent = false
        password = ""
        onMode(next)
    }

    fun submit() {
        if (busy) return
        focus.clearFocus()
        error = when {
            address.isBlank() -> "Enter your email."
            mode != EmailMode.Reset && password.isEmpty() -> "Enter your password."
            mode == EmailMode.SignUp && password.length < MIN_PASSWORD -> "Pick a longer password: at least $MIN_PASSWORD characters."
            else -> null
        }
        if (error != null) return
        working = true
        scope.launch {
            try {
                when (mode) {
                    EmailMode.Reset -> {
                        Oriel.auth.sendPasswordReset(address.trim())
                        resetSent = true
                    }
                    EmailMode.SignIn -> {
                        Oriel.auth.signInWithPassword(address, password)
                        afterSignIn()
                    }
                    EmailMode.SignUp -> {
                        Oriel.auth.createAccount(name, address, password)
                        afterSignIn()
                    }
                }
            } catch (e: Exception) {
                error = emailError(e)
            } finally {
                working = false
            }
        }
    }

    if (mode == EmailMode.Reset && resetSent) {
        Column(
            Modifier.fillMaxWidth().background(Palette.colors.surface, RoundedCornerShape(24.dp)).padding(20.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text("Check your inbox", style = MaterialTheme.typography.titleMedium)
            Spacer(Modifier.height(6.dp))
            Text("If there's an Oriel account for ${address.trim()}, a link to set a new password is on its way.",
                style = MaterialTheme.typography.bodyMedium, color = Palette.colors.muted, textAlign = TextAlign.Center)
            TextAction("Back to sign in", { switchTo(EmailMode.SignIn) })
        }
        return
    }

    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        if (mode == EmailMode.SignUp) {
            OrielTextField(
                name, { name = it.take(60) }, Modifier.semantics { contentType = ContentType.PersonFullName },
                placeholder = "Name", leadingIcon = OrielIcons.User, enabled = !busy,
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Words, imeAction = ImeAction.Next),
                keyboardActions = KeyboardActions(onNext = { focus.moveFocus(FocusDirection.Down) }),
            )
        }
        OrielTextField(
            address, { address = it; error = null },
            Modifier.semantics { contentType = if (mode == EmailMode.SignUp) ContentType.NewUsername else ContentType.Username + ContentType.EmailAddress },
            placeholder = "Email", leadingIcon = OrielIcons.Mail, enabled = !busy,
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Email, autoCorrectEnabled = false,
                imeAction = if (mode == EmailMode.Reset) ImeAction.Send else ImeAction.Next,
            ),
            keyboardActions = KeyboardActions(onNext = { focus.moveFocus(FocusDirection.Down) }, onSend = { submit() }),
        )
        if (mode != EmailMode.Reset) {
            OrielTextField(
                password, { password = it; error = null },
                Modifier.semantics { contentType = if (mode == EmailMode.SignUp) ContentType.NewPassword else ContentType.Password },
                placeholder = if (mode == EmailMode.SignUp) "Password ($MIN_PASSWORD+ characters)" else "Password",
                leadingIcon = OrielIcons.Lock, enabled = !busy,
                visualTransformation = if (shown) VisualTransformation.None else PasswordVisualTransformation(),
                trailing = { TextAction(if (shown) "Hide" else "Show", { shown = !shown }, color = Palette.colors.muted) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, autoCorrectEnabled = false, imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = { submit() }),
            )
        }
        if (mode == EmailMode.SignIn) {
            TextAction("Forgot password?", { switchTo(EmailMode.Reset) }, Modifier.align(Alignment.End), color = Palette.colors.muted, enabled = !busy)
        }

        error?.let {
            Text(it, Modifier.fillMaxWidth(), style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.error, textAlign = TextAlign.Center)
        }

        PrimaryButton(
            when (mode) {
                EmailMode.SignIn -> if (working) "Signing in…" else "Sign in"
                EmailMode.SignUp -> if (working) "Creating your account…" else "Create account"
                EmailMode.Reset -> if (working) "Sending…" else "Send reset link"
            },
            onClick = ::submit, modifier = Modifier.fillMaxWidth(), busy = working, enabled = !SignInState.working,
        )

        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
            Text(
                when (mode) {
                    EmailMode.SignIn -> "New to Oriel?"
                    EmailMode.SignUp -> "Already have an account?"
                    EmailMode.Reset -> "Remembered it?"
                },
                style = MaterialTheme.typography.bodyMedium, color = Palette.colors.muted,
            )
            TextAction(if (mode == EmailMode.SignIn) "Create an account" else "Sign in",
                { switchTo(if (mode == EmailMode.SignIn) EmailMode.SignUp else EmailMode.SignIn) }, enabled = !busy)
        }
    }
}

/** Words for a failed email sign-in. Firebase's REST errors are codes like `INVALID_LOGIN_CREDENTIALS`. */
private fun emailError(e: Exception): String {
    if (e !is FirebaseException) return "Couldn't sign you in. Please try again."
    if (e.failure == FirebaseFailure.Network) return "You seem to be offline. Try again."
    val m = e.message.orEmpty()
    return when {
        listOf("INVALID_LOGIN_CREDENTIALS", "INVALID_PASSWORD", "EMAIL_NOT_FOUND").any { it in m } ->
            "That email and password don't match. If you joined with Google, continue with Google instead."
        "EMAIL_EXISTS" in m -> "There's already an Oriel account with this email. Sign in instead (with Google, if that's how you joined)."
        "INVALID_EMAIL" in m || "MISSING_EMAIL" in m -> "That doesn't look like an email address."
        "MISSING_PASSWORD" in m -> "Enter your password."
        "WEAK_PASSWORD" in m || "PASSWORD_DOES_NOT_MEET_REQUIREMENTS" in m -> "Pick a longer password: at least $MIN_PASSWORD characters."
        "TOO_MANY_ATTEMPTS" in m -> "Too many tries. Wait a few minutes, or reset your password."
        "USER_DISABLED" in m -> "This account has been turned off. Contact support if that's a mistake."
        "OPERATION_NOT_ALLOWED" in m -> "Signing in with email isn't available right now. Continue with Google instead."
        else -> "Couldn't sign you in. Please try again."
    }
}
