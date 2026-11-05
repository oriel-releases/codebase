package uz.akbarjon.oriel.ui

import uz.akbarjon.oriel.data.Position
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.foundation.clickable
import androidx.compose.animation.scaleOut
import androidx.compose.animation.scaleIn
import androidx.compose.ui.unit.Dp
import kotlin.math.roundToInt
import uz.akbarjon.oriel.ui.Motion
import uz.akbarjon.oriel.ui.Motion.fadeThrough
import uz.akbarjon.oriel.ui.Motion.slideThrough
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
import androidx.compose.ui.layout.onGloballyPositioned
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
        Crossfade(photo?.thumb(48, 96), animationSpec = tween(900, easing = Motion.Emphasized), label = "backdrop") { url ->
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
    val entry by produceState(Oriel.history.lastLatest, version) { value = Oriel.history.latest() }

    // The position editor opens out of the photo frame and folds back into it; the frame hides while it's covered.
    val origin = remember { EditorOrigin() }
    val previewPx = with(LocalDensity.current) { HomePreviewWidth.roundToPx() }
    origin.pictureFor = { position -> photo?.thumb(previewPx, previewPx * screenH / screenW, position).orEmpty() }
    // Crop opens at once: what it needs is loaded as each photo shows (a small picture and a few tiny ones, from the CDN).
    LaunchedEffect(photo?.id) { photo?.let { prepareEditor(it, origin.pictureFor?.invoke(Position.Smart)) } }
    val view = androidx.compose.ui.platform.LocalView.current
    // The rest of Home's actions (favorite, share, position, lock), behind the header's more button.
    var more by remember { mutableStateOf(false) }
    val frame = Modifier
        .onGloballyPositioned { origin.bounds = it.boundsOnScreen(view) }
        .graphicsLayer { alpha = if (origin.covered) 0f else 1f }

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
            WallpaperPreview(photo, screenW, screenH, Modifier.fillMaxHeight().padding(vertical = 4.dp).then(frame), onLike, credit = false,
                chromeIn = !origin.covered)
            Spacer(Modifier.width(24.dp))
            Column(Modifier.weight(1f).widthIn(max = 460.dp), verticalArrangement = Arrangement.Center) {
                // On its side there's room beside the photo: the actions are right there, no more button.
                Header(onMore = null)
                if (photo != null) Credit(photo, LocalContext.current, Modifier.padding(bottom = 12.dp), color = Dim)
                Spacer(Modifier.height(8.dp))
                Controls(photo, entry, origin, more, onMoreClosed = { more = false }, inline = true)
            }
        }
    } else {
        Column(Modifier.fillMaxSize().padding(bottom = bottomBarSpace()), horizontalAlignment = Alignment.CenterHorizontally) {
            Header(onMore = { more = true }, Modifier.padding(start = 24.dp, end = 16.dp))
            Box(Modifier.weight(1f).fillMaxWidth().padding(top = 4.dp, bottom = 12.dp), contentAlignment = Alignment.Center) {
                WallpaperPreview(photo, screenW, screenH, Modifier.fillMaxHeight().then(frame), onLike, chromeIn = !origin.covered)
            }
            // Edge to edge with the navigation bar under it (the same 24 dp from the screen's sides).
            Controls(photo, entry, origin, more, onMoreClosed = { more = false }, Modifier.padding(horizontal = 24.dp))
        }
    }
}

@Composable
private fun Header(onMore: (() -> Unit)?, modifier: Modifier = Modifier) {
    val haptics = LocalHapticFeedback.current
    val locked = Oriel.store.settings.collectAsState().value.paused
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
        // Upright, Lock lives behind more: while it's on, a badge beside it says so (like BETA's; only a sign, not a
        // button). On its side the Lock button is right there, solid while locked: no badge.
        AnimatedVisibility(locked && onMore != null, enter = fadeIn() + scaleIn(initialScale = 0.8f), exit = fadeOut() + scaleOut(targetScale = 0.8f)) {
            Text(
                "LOCKED", style = MaterialTheme.typography.labelSmall, color = White,
                modifier = Modifier.padding(end = 10.dp).clip(CircleShape).background(White.copy(alpha = 0.18f))
                    .padding(horizontal = 7.dp, vertical = 2.dp),
            )
        }
        if (onMore != null) RoundAction(OrielIcons.More, "More", size = 40.dp) {
            haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
            onMore()
        }
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
/** How wide Home's preview picture is asked for (the editor loads the same one before folding back onto it). */
internal val HomePreviewWidth = 320.dp

/** The photo page's feed in [WallpaperPreview]: the neighbors, and how far the pictures are moved (pixels, up < 0). */
internal class PreviewFeed(val previous: Photo?, val next: Photo?, val offset: () -> Float)

@Composable
internal fun WallpaperPreview(
    photo: Photo?, screenW: Int, screenH: Int, modifier: Modifier,
    /** Double tap to favorite, with a star burst; null where a photo can't be favorited (Explore, search): no stars. */
    onLike: (() -> Unit)?, credit: Boolean = true,
    /** Shown at once under the photo while it loads (the tile's picture the page opened from: already loaded). */
    under: String? = null,
    /** The photo's width in pixels, when it's shown bigger than Home's (the photo page). */
    widthPx: Int? = null,
    /** The photo page's feed: the photos before and after, sliding through the frame (which itself stays still). */
    feed: PreviewFeed? = null,
    /** Home's phone frame (rounded, edged, the handle drawn on); false fills its space bare (the photo page, full screen). */
    framed: Boolean = true,
    /**
     * The credit pill and the gesture line on the photo. False while the position editor covers Home's photo: when it
     * folds back, they come in (the pill rising, the line growing from its middle) instead of popping on.
     */
    chromeIn: Boolean = true,
) {
    val context = LocalContext.current
    val previewWidthPx = widthPx?.takeIf { it > 0 } ?: with(LocalDensity.current) { HomePreviewWidth.roundToPx() }
    // A new position (the editor) crops the preview again.
    uz.akbarjon.oriel.core.Positions.version.collectAsState().value
    val bursts = remember { mutableStateListOf<Burst>() }
    val haze = remember { HazeState() }
    val gestures = gestureNavigation()
    val chrome by androidx.compose.animation.core.animateFloatAsState(
        if (chromeIn) 1f else 0f,
        if (chromeIn) tween(460, delayMillis = 40, easing = FastOutSlowInEasing) else androidx.compose.animation.core.snap(),
        label = "chrome",
    )

    BoxWithConstraints(if (framed) modifier.aspectRatio(screenW.toFloat() / screenH, matchHeightConstraintsFirst = true) else modifier) {
        val w = maxWidth
        val h = maxHeight
        val shape = ScreenShape
        Box(
            Modifier.fillMaxSize().let { if (framed) it.clip(shape).background(White.copy(alpha = 0.08f)).border(1.5.dp, White.copy(alpha = 0.22f), shape) else it }
                .pointerInput(onLike != null) {
                    val like = onLike ?: return@pointerInput
                    // A tap within a moment of the last one is a "like": a star at the finger, every time, no limit.
                    var last = 0L
                    awaitEachGesture {
                        val down = awaitFirstDown()
                        val now = down.uptimeMillis
                        if (now - last < 350) {
                            bursts += Burst(System.nanoTime(), down.position, Random.nextFloat() * 30f - 15f)
                            like()
                        }
                        last = now
                    }
                },
        ) {
            /** One photo's picture, filling the frame ([under]: the tile's picture, shown at once while it loads). */
            @Composable
            fun Picture(p: Photo, under: String?) {
                val position = uz.akbarjon.oriel.core.Positions.of(p.id)
                if (under != null) RemoteImage(under, null, Modifier.fillMaxSize(), placeholder = parseColor(p.color) ?: White.copy(alpha = 0.08f))
                RemoteImage(
                    p.thumb(previewWidthPx, previewWidthPx * screenH / screenW, position), p.description, Modifier.fillMaxSize(),
                    placeholder = if (under != null) androidx.compose.ui.graphics.Color.Transparent else parseColor(p.color) ?: White.copy(alpha = 0.08f),
                    // Unsplash crops its photos on its side; an AI wallpaper is whole, so the preview is placed here.
                    focus = if (p.isAi) (position as? uz.akbarjon.oriel.data.Position.Focus)?.let { it.x to it.y } ?: (0.5f to 0.5f) else null,
                )
            }
            Box(Modifier.fillMaxSize().hazeSource(haze)) {
                if (feed != null) {
                    // The feed: the previous picture above, this one, the next below, moved by the finger. Each keeps its
                    // place (keyed by photo), so the one sliding in already has its picture when it becomes this one.
                    val column = listOfNotNull(feed.previous?.let { -1 to it }, photo?.let { 0 to it }, feed.next?.let { 1 to it })
                    for ((slot, p) in column) {
                        key(p.id) {
                            Box(Modifier.fillMaxSize().graphicsLayer { translationY = feed.offset() + slot * size.height }) {
                                Picture(p, if (slot == 0) under else null)
                            }
                        }
                    }
                } else AnimatedContent(photo, transitionSpec = {
                    fadeIn(Motion.enter(Motion.LONG)) togetherWith fadeOut(Motion.exit(Motion.LONG))
                }, label = "preview") { p ->
                    Box(Modifier.fillMaxSize()) {
                        if (p != null) Picture(p, under)
                        else Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { Logo(32.dp, tint = White.copy(alpha = 0.5f)) }
                    }
                }
            }
            // The credit Unsplash asks for, on a blurred pill at the bottom of the photo.
            if (photo != null && credit) {
                Box(
                    Modifier.align(Alignment.BottomCenter).padding(start = 12.dp, end = 12.dp, bottom = if (gestures) h * 0.05f else 12.dp)
                        .graphicsLayer {
                            alpha = chrome
                            translationY = (1 - chrome) * 14.dp.toPx()
                            scaleX = 0.94f + 0.06f * chrome
                            scaleY = scaleX
                        }
                        .clip(CircleShape)
                        .hazeEffect(haze, HazeStyle(backgroundColor = Color.Black, tint = HazeTint(Color.Black.copy(alpha = 0.25f)), blurRadius = 18.dp, noiseFactor = 0f))
                        .padding(horizontal = 12.dp, vertical = 6.dp),
                ) {
                    if (feed == null) Credit(photo, context, color = White)
                    else {
                        // Swiping: the pill turns into the next photo's credit with the finger, its width easing from one
                        // name to the other and the names crossfading, so it feels part of the swipe.
                        val heightPx = with(LocalDensity.current) { h.toPx() }
                        val toward = { val o = feed.offset(); if (o < 0) feed.next else if (o > 0) feed.previous else null }
                        CreditMorph(
                            from = { Credit(photo, context, color = White) },
                            to = toward()?.let { other -> { Credit(other, context, color = White) } },
                            progress = { (kotlin.math.abs(feed.offset()) / heightPx).coerceIn(0f, 1f) },
                        )
                    }
                }
            }
            if (gestures && framed) {
                Box(Modifier.align(Alignment.BottomCenter).padding(bottom = h * 0.018f).width(w * 0.3f).height(4.dp)
                    .graphicsLayer { alpha = chrome; scaleX = 0.3f + 0.7f * chrome }
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

/** A line of status when there's something to say, then Change now; and the more sheet, when it's open. */
@Composable
private fun Controls(
    photo: Photo?, entry: HistoryEntry?, origin: EditorOrigin, more: Boolean, onMoreClosed: () -> Unit, modifier: Modifier = Modifier,
    /** The phone on its side: favorite, share, position and lock as buttons beside Change now, instead of in the sheet. */
    inline: Boolean = false,
) {
    val context = LocalContext.current
    val snackbar = LocalSnackbar.current
    val haptics = LocalHapticFeedback.current
    val scope = rememberCoroutineScope()
    val settings by Oriel.store.settings.collectAsState()
    val state by Oriel.store.state.collectAsState()
    val busy by Oriel.wallpapers.busy.collectAsState()
    val phase by Oriel.wallpapers.phase.collectAsState()
    val spin = remember { Animatable(0f) }
    val busySpin by rememberInfiniteTransition(label = "spin").animateFloat(0f, 360f, infiniteRepeatable(tween(900, easing = LinearEasing)), label = "busySpin")
    val working = busy || phase != null
    // The editor shows over the whole app (PositionEditorHost), unfolding out of Home's photo.
    val adjust = { photo?.let { PositionEditors.open = EditorRequest(it, origin) }; Unit }
    val favorite: Boolean? = entry?.isFavorite
    val setFavorite = { fav: Boolean ->
        entry?.photo?.id?.let { id ->
            haptics.performHapticFeedback(HapticFeedbackType.Confirm)
            uz.akbarjon.oriel.core.FavoritesSync.set(id, fav)
        }
        Unit
    }
    val setLocked = { lock: Boolean ->
        // Lock: the schedule pauses on every device.
        haptics.performHapticFeedback(if (lock) HapticFeedbackType.ToggleOn else HapticFeedbackType.ToggleOff)
        Oriel.store.update { it.copy(paused = lock) }
        Scheduler.restart(context)
    }
    // Its link is made first (and the photo saved to send), so it takes a moment.
    val share: suspend () -> Unit = {
        val p = photo
        if (p != null) try {
            sharePhoto(context, p, "A wallpaper for you: ${Sharing.shareLink(p)}", "Share this wallpaper")
        } catch (e: OrielException) {
            Oriel.scope.launch(Dispatchers.Main) { snackbar.showSnackbar(e.message ?: "Couldn't share") }
        }
    }
    var sharing by remember { mutableStateOf(false) }

    Column(modifier.fillMaxWidth()) {
        val problem = state.followProblem.takeIf { settings.isFollowing } ?: state.lastError.takeIf { !settings.isFollowing }
        // (A waiting update shows as the navigation bar's Update item, not here.)
        val status: Pair<ImageVector, String>? = when {
            problem != null -> OrielIcons.Alert to problem
            settings.source == Source.Friend -> OrielIcons.Users to "Showing ${settings.followName ?: "a friend"}'s wallpaper"
            else -> null
        }
        AnimatedVisibility(status != null, enter = fadeIn() + expandVertically(), exit = fadeOut() + shrinkVertically()) {
            val shown = status ?: (OrielIcons.Info to "")
            Row(
                Modifier.fillMaxWidth().padding(start = 4.dp, end = 4.dp, bottom = 10.dp),
                verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center,
            ) {
                Icon(shown.first, null, tint = White, modifier = Modifier.size(15.dp))
                Spacer(Modifier.width(8.dp))
                AnimatedContent(shown.second, transitionSpec = { fadeIn(tween(220)) togetherWith fadeOut(tween(120)) }, label = "status") {
                    Text(it, style = MaterialTheme.typography.bodySmall, color = White, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
        }

        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            // Upright, the one thing Home is for; the rest is behind the header's more button.
            if (inline) {
                RoundAction(if (favorite == true) OrielIcons.StarFilled else OrielIcons.Star,
                    if (favorite == true) "Remove from favorites" else "Add to favorites",
                    active = favorite == true, activeTint = StarGold, enabled = favorite != null) { setFavorite(favorite != true) }
                RoundAction(OrielIcons.Share, "Share this wallpaper", enabled = photo != null && !photo.isAi && !sharing, busy = sharing) {
                    sharing = true
                    Oriel.scope.launch(Dispatchers.Main) { try { share() } finally { sharing = false } }
                }
                RoundAction(OrielIcons.Crop, "Adjust the position", enabled = photo != null) {
                    haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
                    adjust()
                }
                RoundAction(if (settings.paused) OrielIcons.Lock else OrielIcons.Unlock,
                    if (settings.paused) "Unlock: let it change" else "Lock: keep this wallpaper", active = settings.paused) { setLocked(!settings.paused) }
            }
            PrimaryButton(
                phase?.text ?: if (busy) "Changing…" else if (photo == null) "Set my first" else "Change now",
                onClick = {
                    haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                    scope.launch { spin.snapTo(0f); spin.animateTo(360f, tween(600, easing = FastOutSlowInEasing)) }
                    Oriel.scope.launch(Dispatchers.Main) {
                        when (val result = Oriel.wallpapers.changeNow()) {
                            // A soft buzz the moment it's on the screen (the new wallpaper says the rest).
                            is ChangeResult.Applied -> haptics.performHapticFeedback(HapticFeedbackType.Confirm)
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

    if (more) MoreSheet(
        photo = photo, locked = settings.paused, onDismiss = onMoreClosed,
        favorite = favorite, onFavorite = setFavorite,
        onShare = share,
        onLock = setLocked,
        onPosition = adjust,
    )
}

/**
 * What Home keeps behind its more button, for this wallpaper, in two groups like Settings: what it is (a favorite,
 * locked: switches that flip in place) and what to do with it (share, position: they close the sheet and go).
 */
@Composable
private fun MoreSheet(
    photo: Photo?, locked: Boolean, onDismiss: () -> Unit, onShare: suspend () -> Unit, onLock: (Boolean) -> Unit, onPosition: () -> Unit,
    /** Whether it's a favorite; null when it can't be one (not in History yet). */
    favorite: Boolean?, onFavorite: (Boolean) -> Unit,
) {
    OrielSheet(onDismiss, title = "Wallpaper") {
        val c = Palette.colors
        val close = LocalSheetClose.current
        val leave = { then: () -> Unit -> { close?.invoke { onDismiss(); then() } ?: run { onDismiss(); then() } } }
        Group {
            val fav = favorite == true
            SettingRow("Favorite", leading = { BurstingStar(fav) },
                onClick = favorite?.let { { onFavorite(!it) } }) { OrielSwitch(fav, favorite != null) { onFavorite(it) } }
            RowDivider()
            SettingRow("Lock", icon = if (locked) OrielIcons.Lock else OrielIcons.Unlock, onClick = { onLock(!locked) }) {
                OrielSwitch(locked) { onLock(it) }
            }
        }
        Spacer(Modifier.height(12.dp))
        Group {
            if (photo != null && !photo.isAi) {
                // Its link is made first (and the photo saved to send): a spinner meanwhile, no second tap; once the
                // phone's share menu is up, this sheet goes.
                var sharing by remember { mutableStateOf(false) }
                val scope = rememberCoroutineScope()
                SettingRow("Share", icon = OrielIcons.Share, onClick = if (sharing) null else ({
                    sharing = true
                    scope.launch {
                        onShare()
                        leave {}()
                    }
                }), leading = if (sharing) ({
                    CircularProgressIndicator(Modifier.size(21.dp).padding(2.dp), color = c.ink, strokeWidth = 2.dp)
                    Spacer(Modifier.width(2.dp))
                }) else null)
                RowDivider()
            }
            SettingRow("Position", icon = OrielIcons.Crop, onClick = if (photo != null) leave(onPosition) else null)
        }
        Spacer(Modifier.height(8.dp))
    }
}

/** A small round see-through button on the photo; white while [active] (locked, a favorite). */
@Composable
private fun RoundAction(
    icon: ImageVector, description: String, active: Boolean = false, activeTint: Color = Ink, enabled: Boolean = true,
    pop: Boolean = false, size: Dp = 48.dp, busy: Boolean = false, onClick: () -> Unit,
) {
    val bg by animateColorAsState(if (active) White else White.copy(alpha = 0.14f), label = "action")
    val fg by animateColorAsState(if (active) activeTint else White, label = "actionIcon")
    val bounce = remember { Animatable(1f) }
    LaunchedEffect(pop) { if (pop) bounce.animateTo(1f, keyframes { durationMillis = 420; 1.35f at 120; 0.9f at 260 }) }
    Box(
        Modifier.size(size).clip(CircleShape).background(bg).border(1.dp, White.copy(alpha = 0.14f), CircleShape)
            .springClick(enabled, pressed = 0.85f, onClick = onClick).graphicsLayer { alpha = if (enabled) 1f else 0.45f },
        contentAlignment = Alignment.Center,
    ) {
        if (busy) CircularProgressIndicator(Modifier.size(18.dp), color = fg, strokeWidth = 2.dp)
        else Icon(icon, description, tint = fg, modifier = Modifier.size(19.dp).graphicsLayer { scaleX = bounce.value; scaleY = bounce.value })
    }
}

/**
 * One credit turning into another as [progress] goes 0 to 1: as wide as [from] at 0 and as [to] at 1 (in between, in
 * between), each faded by how far along it is. The one more visible is on top, so it's the one a tap reaches.
 */
@Composable
private fun CreditMorph(from: @Composable () -> Unit, to: (@Composable () -> Unit)?, progress: () -> Float) {
    androidx.compose.ui.layout.Layout(content = { from(); to?.invoke() }) { measurables, constraints ->
        val loose = constraints.copy(minWidth = 0)
        val a = measurables[0].measure(loose)
        val b = measurables.getOrNull(1)?.measure(loose)
        val p = if (b == null) 0f else progress()
        val width = (a.width + ((b?.width ?: a.width) - a.width) * p).roundToInt().coerceIn(constraints.minWidth, constraints.maxWidth)
        val height = maxOf(a.height, b?.height ?: 0)
        layout(width, height) {
            fun place(it: androidx.compose.ui.layout.Placeable, alpha: Float) =
                it.placeWithLayer((width - it.width) / 2, (height - it.height) / 2) { this.alpha = alpha }
            if (b == null) place(a, 1f)
            else if (p < 0.5f) { place(b, p); place(a, 1 - p) } else { place(a, 1 - p); place(b, p) }
        }
    }
}

/** The More sheet's star: starred, it pops and a ring of little gold sparks flies out of it and fades. */
@Composable
private fun BurstingStar(on: Boolean) {
    val c = Palette.colors
    val pop = remember { Animatable(1f) }
    val sparks = remember { Animatable(1f) }
    var seen by remember { mutableStateOf(on) }
    LaunchedEffect(on) {
        if (on && !seen) {
            launch { pop.animateTo(1f, keyframes { durationMillis = 420; 1.4f at 140; 0.9f at 280 }) }
            sparks.snapTo(0f)
            sparks.animateTo(1f, tween(520, easing = FastOutSlowInEasing))
        }
        seen = on
    }
    Box(Modifier.size(21.dp), contentAlignment = Alignment.Center) {
        androidx.compose.foundation.Canvas(Modifier.size(21.dp)) {
            val t = sparks.value
            if (t >= 1f) return@Canvas
            val radius = size.minDimension / 2 * (0.7f + 0.9f * t)
            for (i in 0 until 6) {
                val a = Math.toRadians(i * 60.0 - 90).toFloat()
                drawCircle(StarGold.copy(alpha = 1f - t), 2.2.dp.toPx() * (1f - t * 0.5f),
                    center + Offset(kotlin.math.cos(a) * radius, kotlin.math.sin(a) * radius))
            }
        }
        Icon(if (on) OrielIcons.StarFilled else OrielIcons.Star, null, tint = if (on) StarGold else c.ink,
            modifier = Modifier.size(21.dp).graphicsLayer { scaleX = pop.value; scaleY = pop.value })
    }
}

