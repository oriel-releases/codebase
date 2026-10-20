package uz.akbarjon.oriel

import androidx.compose.foundation.layout.BoxWithConstraints
import uz.akbarjon.oriel.ui.LocalNavOnSide
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat
import dev.chrisbanes.haze.HazeState
import dev.chrisbanes.haze.hazeSource
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import uz.akbarjon.oriel.core.Scheduler
import uz.akbarjon.oriel.core.Pulse
import uz.akbarjon.oriel.core.SettingsSync
import uz.akbarjon.oriel.core.Updater
import uz.akbarjon.oriel.data.FirebaseException
import uz.akbarjon.oriel.data.FirebaseFailure
import uz.akbarjon.oriel.data.Photo
import uz.akbarjon.oriel.data.Unsplash
import uz.akbarjon.oriel.ui.HistoryScreen
import uz.akbarjon.oriel.ui.HomeScreen
import uz.akbarjon.oriel.ui.LocalSnackbar
import uz.akbarjon.oriel.ui.NavBar
import uz.akbarjon.oriel.ui.OrielTheme
import uz.akbarjon.oriel.ui.Palette
import uz.akbarjon.oriel.ui.PhotoSheet
import uz.akbarjon.oriel.ui.SearchScreen
import uz.akbarjon.oriel.ui.SettingsScreen
import uz.akbarjon.oriel.ui.SignInScreen
import uz.akbarjon.oriel.ui.SignInState
import uz.akbarjon.oriel.ui.Tab
import uz.akbarjon.oriel.ui.NavBarHeight
import uz.akbarjon.oriel.ui.NavBarMargin
import uz.akbarjon.oriel.ui.HomeBackdrop
import uz.akbarjon.oriel.ui.currentPhoto
import uz.akbarjon.oriel.ui.afterSignIn

/** A photo someone shared (`oriel://wallpaper?id=...&from=Name`), waiting to be shown. */
private class SharedLink(val photoId: String, val from: String)

class MainActivity : ComponentActivity() {
    private var shared by mutableStateOf<SharedLink?>(null)

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)
        if (savedInstanceState == null) handle(intent)
        setContent { OrielTheme { App() } }
    }

    override fun onStop() {
        Updater.foreground = false
        Pulse.stop()
        super.onStop()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handle(intent)
    }

    override fun onStart() {
        super.onStart()
        // Every launch looks for an update (like Windows and Linux); one waiting for a tap is shown right away.
        Updater.foreground = true
        // While Oriel is on screen, a wallpaper set on another device shows here within a second.
        Pulse.start()
        // The settings every device shares: sent on every change, and the account's brought here now.
        SettingsSync.start()
        if (Oriel.auth.isSignedIn) Oriel.scope.launch { SettingsSync.pull(); uz.akbarjon.oriel.core.FavoritesSync.pull() }
        if (!Updater.confirm(this)) Updater.checkSoon(this)
        if (!Oriel.auth.isSignedIn) return
        Scheduler.reschedule(this)
        Oriel.scope.launch {
            // Now and then make sure this sign-in is still valid (it can be ended from the account page).
            val now = System.currentTimeMillis()
            if (now - Oriel.store.state.value.lastValidatedAt > 6 * 3_600_000L) {
                if (Oriel.auth.validate()) Oriel.store.updateState { it.copy(lastValidatedAt = now) } else Scheduler.cancelAll(Oriel.app)
            }
            // Opening the app is a free moment to pick up a new followed wallpaper.
            Oriel.following.check()
        }
    }

    private fun handle(intent: Intent?) {
        val uri = intent?.data ?: return
        if (uri.scheme != "oriel") return
        when (uri.host) {
            "auth" -> {
                // `token`: a Google ID token. `refresh`: the website's session for an account without Google.
                val google = uri.getQueryParameter("token")?.takeIf { it.isNotBlank() }
                val session = uri.getQueryParameter("refresh")?.takeIf { it.isNotBlank() }
                when {
                    google != null -> signIn(uri) { Oriel.auth.signIn(google) }
                    session != null -> signIn(uri) { Oriel.auth.signInWithSession(session) }
                }
            }
            "wallpaper" -> photoId(uri.getQueryParameter("id"))?.let { shared = SharedLink(it, cleanName(uri.getQueryParameter("from"))) }
        }
    }

    private fun signIn(uri: Uri, start: suspend () -> Unit) {
        if (SignInState.working) return
        SignInState.working = true
        SignInState.error = null
        // A wallpaper shared before Oriel was installed rides along the sign-in link (`&share=ID&from=Name`).
        val share = photoId(uri.getQueryParameter("share"))?.let { SharedLink(it, cleanName(uri.getQueryParameter("from"))) }
        Oriel.scope.launch {
            try {
                start()
                withContext(Dispatchers.Main) { if (share != null) shared = share }
                afterSignIn()
            } catch (e: FirebaseException) {
                SignInState.error = if (e.failure == FirebaseFailure.Network) "You seem to be offline. Try again."
                else "That sign-in link has expired. Sign in again."
            } catch (e: Exception) {
                SignInState.error = "Couldn't sign you in. Please try again."
            } finally {
                SignInState.working = false
            }
        }
    }

    @Composable
    private fun App() {
        val account by Oriel.auth.account.collectAsState()
        val snackbar = remember { SnackbarHostState() }
        var tab by rememberSaveable { mutableStateOf(Tab.Home) }

        CompositionLocalProvider(LocalSnackbar provides snackbar) {
            BoxWithConstraints(Modifier.fillMaxSize().background(Palette.colors.paper)) {
                // On its side, the phone gets the navigation bar on the right edge.
                val side = maxWidth > maxHeight
                val sideSpace = if (side) NavBarHeight + NavBarMargin * 2 else 0.dp
                if (account == null) {
                    SignInScreen()
                } else {
                    // Home sits on its wallpaper, blurred, edge to edge; the other tabs on plain paper.
                    val onHome = tab == Tab.Home
                    val photo = currentPhoto()
                    val haze = remember { HazeState() }
                    val dark = Palette.colors.dark
                    val view = LocalView.current
                    SideEffect {
                        // Light status bar icons over the photo.
                        WindowCompat.getInsetsController(window, view).isAppearanceLightStatusBars = !onHome && !dark
                    }
                    // Everything under the navigation bar is its blur source.
                    Box(Modifier.fillMaxSize().hazeSource(haze)) {
                    AnimatedVisibility(onHome, enter = fadeIn(tween(300)), exit = fadeOut(tween(200))) { HomeBackdrop(photo) }
                    AnimatedContent(
                        tab, Modifier.fillMaxSize().statusBarsPadding().padding(end = sideSpace),
                        transitionSpec = {
                            (fadeIn(tween(220, delayMillis = 40)) + scaleIn(tween(280), initialScale = 0.985f)) togetherWith fadeOut(tween(120))
                        },
                        label = "tab",
                    ) { t ->
                        CompositionLocalProvider(LocalNavOnSide provides side) { when (t) {
                            Tab.Home -> HomeScreen()
                            Tab.Search -> SearchScreen()
                            Tab.Create -> uz.akbarjon.oriel.ui.CreateScreen()
                            Tab.History -> HistoryScreen()
                            Tab.Settings -> SettingsScreen(onSignedOut = { tab = Tab.Home })
                        } }
                    }
                    }
                    NavBar(tab, { tab = it }, haze, Modifier.align(if (side) Alignment.CenterEnd else Alignment.BottomCenter),
                        onPhoto = onHome, vertical = side)
                }
                SnackbarHost(
                    snackbar,
                    Modifier.align(Alignment.BottomCenter).navigationBarsPadding()
                        .padding(bottom = if (account != null && !side) NavBarHeight + NavBarMargin * 2 else 16.dp)
                        .padding(start = 16.dp, end = 16.dp + sideSpace),
                ) { data ->
                    Snackbar(data, shape = RoundedCornerShape(18.dp), containerColor = Palette.colors.ink, contentColor = Palette.colors.paper)
                }
            }
            if (account != null) shared?.let { link -> SharedSheet(link) { shared = null } }
        }
    }

    /** A shared photo: looked up once (one Unsplash request), then offered with the sender's name. */
    @Composable
    private fun SharedSheet(link: SharedLink, onDismiss: () -> Unit) {
        val photo by produceState<Photo?>(null, link) {
            value = try {
                Unsplash.photo(link.photoId)
            } catch (_: Exception) {
                null
            }
        }
        photo?.let {
            PhotoSheet(it, topic = "shared", onDismiss = onDismiss,
                heading = if (link.from.isNotEmpty()) "${link.from} sent you a wallpaper" else "A wallpaper for you")
        }
    }

    private fun photoId(value: String?) = value?.takeIf { Regex("^[A-Za-z0-9_-]{6,24}$").matches(it) }

    private fun cleanName(value: String?) = value?.trim()?.substringBefore(' ')?.take(30)?.filter { it.isLetterOrDigit() || it == '-' } ?: ""
}
