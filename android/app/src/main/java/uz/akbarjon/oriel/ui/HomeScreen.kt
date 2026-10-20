package uz.akbarjon.oriel.ui

import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.runtime.key
import dev.chrisbanes.haze.HazeState
import dev.chrisbanes.haze.HazeStyle
import dev.chrisbanes.haze.HazeTint
import dev.chrisbanes.haze.hazeEffect
import dev.chrisbanes.haze.hazeSource
import android.os.Build
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.Crossfade
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.BlurredEdgeTreatment
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shadow
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import kotlin.random.Random
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.ChangeResult
import uz.akbarjon.oriel.core.Scheduler
import uz.akbarjon.oriel.core.Sharing
import uz.akbarjon.oriel.core.UpdateState
import uz.akbarjon.oriel.core.Updater
import uz.akbarjon.oriel.data.HistoryEntry
import uz.akbarjon.oriel.data.OrielException
import uz.akbarjon.oriel.data.Photo
import uz.akbarjon.oriel.data.Source

private val White = Color.White
private val Dim = Color.White.copy(alpha = 0.7f)
private val Ink = Color(0xFF111114)

/**
 * Home's background, edge to edge behind the status bar and the navigation bar: the current wallpaper, blurred and
 * darkened so white text reads on any photo. A handful of pixels are fetched; scaling them up is most of the blur,
 * and Android 12+ smooths the rest.
 */
@Composable
fun HomeBackdrop(photo: Photo?) {
    Box(Modifier.fillMaxSize().background(Color(0xFF0B0B0D))) {
        Crossfade(photo?.thumb(48, 96), animationSpec = tween(900), label = "backdrop") { url ->
            if (url != null) {
                RemoteImage(
                    url, null,
                    Modifier.fillMaxSize().graphicsLayer { scaleX = 1.15f; scaleY = 1.15f }
                        .let { if (Build.VERSION.SDK_INT >= 31) it.blur(36.dp, BlurredEdgeTreatment.Rectangle) else it },
                    placeholder = Color.Transparent,
                )
            }
        }
        Box(Modifier.fillMaxSize().background(Brush.verticalGradient(
            listOf(Color.Black.copy(alpha = 0.35f), Color.Black.copy(alpha = 0.15f), Color.Black.copy(alpha = 0.6f)),
        )))
    }
}

/**
 * Home: the wallpaper, as big as the screen allows, in a phone-shaped frame (tap twice, or keep tapping, to favorite it
 * with bursting stars), and one compact row under it: Share, Lock, Favorite and Change now. No scrolling; with the
 * phone on its side the photo and the controls stand next to each other.
 */
@Composable
fun HomeScreen() {
    val photo = currentPhoto()
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    val side = LocalNavOnSide.current
    val haptics = LocalHapticFeedback.current
    val scope = rememberCoroutineScope()
    val version by Oriel.history.version.collectAsState()
    val entry by produceState<HistoryEntry?>(null, version) { value = Oriel.history.latest() }

    // Tapping it again and again, like liking a video: always "favorite" (never un-favorite), a star for every tap.
    val onLike = {
        entry?.let { e ->
            haptics.performHapticFeedback(HapticFeedbackType.Confirm)
            if (!e.isFavorite) uz.akbarjon.oriel.core.FavoritesSync.set(e.photo.id, true)
        }
        Unit
    }

    if (side) {
        Row(
            Modifier.fillMaxSize().padding(start = 20.dp, end = 8.dp, top = 8.dp, bottom = bottomBarSpace()),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            // The frame is narrow on its side: the credit sits next to it, on one line, instead of inside.
            WallpaperPreview(photo, screenW, screenH, Modifier.fillMaxHeight().padding(vertical = 4.dp), onLike, credit = false)
            Spacer(Modifier.width(24.dp))
            Column(Modifier.weight(1f).widthIn(max = 460.dp), verticalArrangement = Arrangement.Center) {
                Header()
                if (photo != null) Credit(photo, LocalContext.current, Modifier.padding(bottom = 12.dp), color = Dim)
                Spacer(Modifier.height(8.dp))
                Controls(photo, entry)
            }
        }
    } else {
        Column(Modifier.fillMaxSize().padding(bottom = bottomBarSpace()), horizontalAlignment = Alignment.CenterHorizontally) {
            Header(Modifier.padding(horizontal = 24.dp))
            Box(Modifier.weight(1f).fillMaxWidth().padding(top = 4.dp, bottom = 12.dp), contentAlignment = Alignment.Center) {
                WallpaperPreview(photo, screenW, screenH, Modifier.fillMaxHeight(), onLike)
            }
            Controls(photo, entry, Modifier.padding(horizontal = 20.dp))
        }
    }
}

@Composable
private fun Header(modifier: Modifier = Modifier) {
    val account by Oriel.auth.account.collectAsState()
    Row(modifier.fillMaxWidth().height(52.dp), verticalAlignment = Alignment.CenterVertically) {
        Logo(22.dp, tint = White)
        Spacer(Modifier.width(10.dp))
        Text("Oriel", style = MaterialTheme.typography.titleLarge, color = White)
        Spacer(Modifier.width(8.dp))
        Text(
            "BETA", style = MaterialTheme.typography.labelSmall, color = White,
            modifier = Modifier.clip(CircleShape).background(White.copy(alpha = 0.18f)).padding(horizontal = 7.dp, vertical = 2.dp),
        )
        Spacer(Modifier.weight(1f))
        account?.let { a -> ProfilePicture(a.photoUrl, a.displayName.ifBlank { a.email }, 34.dp, ring = White.copy(alpha = 0.35f)) }
    }
}

/** A star bursting from a tap: where it started, and a little tilt of its own. */
private class Burst(val id: Long, val at: Offset, val tilt: Float)

/** True when the phone uses gesture navigation (the line at the bottom), not the three buttons. */
@Composable
private fun gestureNavigation(): Boolean {
    val context = LocalContext.current
    return remember {
        runCatching { android.provider.Settings.Secure.getInt(context.contentResolver, "navigation_mode") == 2 }.getOrDefault(false)
    }
}

/**
 * The wallpaper in a phone-shaped frame with its credit on a blurred pill inside it, and the gesture line at the
 * bottom when the phone uses gestures. Tapping twice favorites it; every further quick tap adds another star.
 */
@Composable
private fun WallpaperPreview(photo: Photo?, screenW: Int, screenH: Int, modifier: Modifier, onLike: () -> Unit, credit: Boolean = true) {
    val context = LocalContext.current
    val previewWidthPx = with(LocalDensity.current) { 320.dp.roundToPx() }
    // A new position (the editor) crops the preview again.
    uz.akbarjon.oriel.core.Positions.version.collectAsState().value
    val bursts = remember { mutableStateListOf<Burst>() }
    val haze = remember { HazeState() }
    val gestures = gestureNavigation()

    BoxWithConstraints(modifier.aspectRatio(screenW.toFloat() / screenH, matchHeightConstraintsFirst = true)) {
        val w = maxWidth
        val h = maxHeight
        val shape = RoundedCornerShape(w * 0.11f)
        Box(
            Modifier.fillMaxSize().clip(shape).background(White.copy(alpha = 0.08f)).border(1.5.dp, White.copy(alpha = 0.22f), shape)
                .pointerInput(Unit) {
                    // A tap within a moment of the last one is a "like": a star at the finger, every time, no limit.
                    var last = 0L
                    awaitEachGesture {
                        val down = awaitFirstDown()
                        val now = down.uptimeMillis
                        if (now - last < 350) {
                            bursts += Burst(System.nanoTime(), down.position, Random.nextFloat() * 30f - 15f)
                            onLike()
                        }
                        last = now
                    }
                },
        ) {
            Box(Modifier.fillMaxSize().hazeSource(haze)) {
                Crossfade(photo, animationSpec = tween(700), label = "preview") { p ->
                    if (p != null) {
                        val position = uz.akbarjon.oriel.core.Positions.of(p.id)
                        RemoteImage(
                            p.thumb(previewWidthPx, previewWidthPx * screenH / screenW, position), p.description, Modifier.fillMaxSize(),
                            placeholder = parseColor(p.color) ?: White.copy(alpha = 0.08f),
                            // Unsplash crops its photos on its side; an AI wallpaper is whole, so the preview is placed here.
                            focus = if (p.isAi) (position as? uz.akbarjon.oriel.data.Position.Focus)?.let { it.x to it.y } ?: (0.5f to 0.5f) else null,
                        )
                    } else {
                        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { Logo(32.dp, tint = White.copy(alpha = 0.5f)) }
                    }
                }
            }
            // The credit Unsplash asks for, on a blurred pill at the bottom of the photo.
            if (photo != null && credit) {
                Box(
                    Modifier.align(Alignment.BottomCenter).padding(start = 12.dp, end = 12.dp, bottom = if (gestures) h * 0.05f else 12.dp)
                        .clip(CircleShape)
                        .hazeEffect(haze, HazeStyle(backgroundColor = Color.Black, tint = HazeTint(Color.Black.copy(alpha = 0.25f)), blurRadius = 18.dp, noiseFactor = 0f))
                        .padding(horizontal = 12.dp, vertical = 6.dp),
                ) { Credit(photo, context, color = White) }
            }
            if (gestures) {
                Box(Modifier.align(Alignment.BottomCenter).padding(bottom = h * 0.018f).width(w * 0.3f).height(4.dp)
                    .clip(CircleShape).background(White.copy(alpha = 0.75f)))
            }
        }
        // Over the frame, so a star near the top can rise past it. Keyed: one finishing never restarts the others.
        bursts.forEach { b -> key(b.id) { StarBurst(b) { bursts.remove(b) } } }
    }
}

@Composable
private fun StarBurst(burst: Burst, onDone: () -> Unit) {
    val progress = remember { Animatable(0f) }
    LaunchedEffect(Unit) {
        progress.animateTo(1f, tween(950, easing = LinearEasing))
        onDone()
    }
    val size = 84.dp
    val half = with(LocalDensity.current) { (size / 2).toPx() }
    val rise = with(LocalDensity.current) { 150.dp.toPx() }
    Icon(
        OrielIcons.StarFilled, null, tint = StarGold,
        modifier = Modifier.size(size).graphicsLayer {
            val t = progress.value
            // Pops in fast, then floats up, keeps growing and fades away.
            val grow = if (t < 0.18f) 0.3f + 0.95f * (t / 0.18f) else 1.25f + 0.75f * ((t - 0.18f) / 0.82f)
            scaleX = grow
            scaleY = grow
            translationX = burst.at.x - half
            translationY = burst.at.y - half - rise * FastOutSlowInEasing.transform(((t - 0.12f) / 0.88f).coerceIn(0f, 1f))
            rotationZ = burst.tilt * (1f - t)
            alpha = if (t < 0.55f) 1f else 1f - (t - 0.55f) / 0.45f
        },
    )
}

/** A line of status when there's something to say, then Share, Lock, Favorite and Change now in one compact row. */
@Composable
private fun Controls(photo: Photo?, entry: HistoryEntry?, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val snackbar = LocalSnackbar.current
    val haptics = LocalHapticFeedback.current
    val scope = rememberCoroutineScope()
    val settings by Oriel.store.settings.collectAsState()
    val state by Oriel.store.state.collectAsState()
    val busy by Oriel.wallpapers.busy.collectAsState()
    val phase by Oriel.wallpapers.phase.collectAsState()
    val update by Updater.state.collectAsState()
    val spin = remember { Animatable(0f) }
    val busySpin by rememberInfiniteTransition(label = "spin").animateFloat(0f, 360f, infiniteRepeatable(tween(900, easing = LinearEasing)), label = "busySpin")
    val working = busy || phase != null
    var adjusting by remember { mutableStateOf(false) }
    if (adjusting && photo != null) PositionEditor(photo) { adjusting = false }

    Column(modifier.fillMaxWidth()) {
        val problem = state.followProblem.takeIf { settings.isFollowing } ?: state.lastError.takeIf { !settings.isFollowing }
        val needsConfirm = update as? UpdateState.NeedsConfirm
        val status: Pair<ImageVector, String>? = when {
            needsConfirm != null -> OrielIcons.Refresh to "Update ${needsConfirm.version} is ready. Tap to install"
            problem != null -> OrielIcons.Alert to problem
            settings.source == Source.Friend -> OrielIcons.Users to "Showing ${settings.followName ?: "a friend"}'s wallpaper"
            settings.paused -> OrielIcons.Lock to "Locked on all your devices"
            else -> null
        }
        AnimatedVisibility(status != null, enter = fadeIn() + expandVertically(), exit = fadeOut() + shrinkVertically()) {
            val shown = status ?: (OrielIcons.Info to "")
            Row(
                Modifier.fillMaxWidth().padding(start = 4.dp, end = 4.dp, bottom = 10.dp)
                    .let { m -> if (needsConfirm != null) m.springClick { if (!Updater.confirm(context)) Updater.checkSoon(context, force = true) } else m },
                verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center,
            ) {
                Icon(shown.first, null, tint = White, modifier = Modifier.size(15.dp))
                Spacer(Modifier.width(8.dp))
                AnimatedContent(shown.second, transitionSpec = { fadeIn(tween(220)) togetherWith fadeOut(tween(120)) }, label = "status") {
                    Text(it, style = MaterialTheme.typography.bodySmall, color = White, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
        }

        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            // Like the desktop apps' Home: share it, keep it (lock: the schedule pauses on every device), favorite it.
            RoundAction(OrielIcons.Share, "Share this wallpaper", enabled = photo != null && !photo.isAi) {
                val p = photo ?: return@RoundAction
                Oriel.scope.launch(Dispatchers.Main) {
                    try {
                        shareText(context, "A wallpaper for you: ${Sharing.shareLink(p)}", "Share this wallpaper")
                    } catch (e: OrielException) {
                        snackbar.showSnackbar(e.message ?: "Couldn't share")
                    }
                }
            }
            RoundAction(if (settings.paused) OrielIcons.Lock else OrielIcons.Unlock,
                if (settings.paused) "Unlock: let it change" else "Lock: keep this wallpaper", active = settings.paused) {
                val lock = !settings.paused
                haptics.performHapticFeedback(if (lock) HapticFeedbackType.ToggleOn else HapticFeedbackType.ToggleOff)
                Oriel.store.update { it.copy(paused = lock) }
                Scheduler.restart(context)
                Oriel.scope.launch(Dispatchers.Main) {
                    snackbar.showSnackbar(if (lock) "Locked on all your devices: this wallpaper stays" else "Unlocked: changes are back on")
                }
            }
            // Where the photo sits on this phone's screen (only this phone's).
            RoundAction(OrielIcons.Crop, "Adjust the position", enabled = photo != null) {
                haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
                adjusting = true
            }
            val favorite = entry?.isFavorite == true
            RoundAction(if (favorite) OrielIcons.StarFilled else OrielIcons.Star, if (favorite) "Remove from favorites" else "Add to favorites",
                active = favorite, activeTint = StarGold, enabled = entry != null, pop = favorite) {
                val id = entry?.photo?.id ?: return@RoundAction
                haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                uz.akbarjon.oriel.core.FavoritesSync.set(id, !favorite)
            }
            PrimaryButton(
                phase?.text ?: if (busy) "Changing…" else if (photo == null) "Set my first" else "Change now",
                onClick = {
                    haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                    scope.launch { spin.snapTo(0f); spin.animateTo(360f, tween(600, easing = FastOutSlowInEasing)) }
                    Oriel.scope.launch(Dispatchers.Main) {
                        when (val result = Oriel.wallpapers.changeNow()) {
                            is ChangeResult.Applied -> snackbar.showSnackbar("New wallpaper set")
                            is ChangeResult.Failed -> snackbar.showSnackbar(result.message)
                            ChangeResult.Skipped -> Unit
                        }
                    }
                },
                modifier = Modifier.weight(1f).height(48.dp),
                enabled = !working, container = White, content = Ink,
                icon = { Icon(OrielIcons.Refresh, null, Modifier.size(17.dp).graphicsLayer { rotationZ = if (working) busySpin else spin.value }) },
            )
        }
    }
}

/** A small round see-through button on the photo; white while [active] (locked, a favorite). */
@Composable
private fun RoundAction(
    icon: ImageVector, description: String, active: Boolean = false, activeTint: Color = Ink, enabled: Boolean = true,
    pop: Boolean = false, onClick: () -> Unit,
) {
    val bg by animateColorAsState(if (active) White else White.copy(alpha = 0.14f), label = "action")
    val fg by animateColorAsState(if (active) activeTint else White, label = "actionIcon")
    val bounce = remember { Animatable(1f) }
    LaunchedEffect(pop) { if (pop) bounce.animateTo(1f, keyframes { durationMillis = 420; 1.35f at 120; 0.9f at 260 }) }
    Box(
        Modifier.size(44.dp).clip(CircleShape).background(bg).border(1.dp, White.copy(alpha = 0.14f), CircleShape)
            .springClick(enabled, pressed = 0.85f, onClick = onClick).graphicsLayer { alpha = if (enabled) 1f else 0.45f },
        contentAlignment = Alignment.Center,
    ) { Icon(icon, description, tint = fg, modifier = Modifier.size(19.dp).graphicsLayer { scaleX = bounce.value; scaleY = bounce.value }) }
}
