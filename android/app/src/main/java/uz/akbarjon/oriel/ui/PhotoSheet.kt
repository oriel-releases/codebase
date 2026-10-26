package uz.akbarjon.oriel.ui

import dev.chrisbanes.haze.hazeSource
import dev.chrisbanes.haze.hazeEffect
import dev.chrisbanes.haze.HazeTint
import dev.chrisbanes.haze.HazeStyle
import dev.chrisbanes.haze.HazeState
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.ui.graphics.Color
import androidx.compose.foundation.layout.displayCutoutPadding
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import androidx.compose.animation.core.AnimationSpec
import androidx.compose.animation.core.animate
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.unit.Constraints
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalDensity
import kotlin.math.roundToInt
import androidx.compose.ui.layout.layout
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.AnimatedVisibilityScope
import androidx.compose.animation.ExperimentalSharedTransitionApi
import androidx.compose.animation.SharedTransitionScope
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.animation.core.spring
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.composed
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.util.VelocityTracker
import kotlin.math.abs
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.ChangeResult
import uz.akbarjon.oriel.data.Photo

/**
 * Sets [photo] as the wallpaper. Keeps going if the screen that asked goes away. Only a failure is said (the new
 * wallpaper says the rest).
 */
fun setAsWallpaper(photo: Photo, topic: String?, snackbar: SnackbarHostState, onDone: () -> Unit = {}) {
    Oriel.scope.launch(Dispatchers.Main) {
        when (val result = Oriel.wallpapers.apply(photo, topic)) {
            is ChangeResult.Applied -> onDone()
            is ChangeResult.Failed -> snackbar.showSnackbar(result.message)
            ChangeResult.Skipped -> Unit
        }
    }
}

// ---- The photo page, and the flight there from the tile that was tapped (like Windows' connected animation) ----

/** How long the photo takes to fly between its tile and its page, both ways. */
private const val FLY = 520
/** How long it takes to fly home into its tile. */
private const val FLY_HOME = 440

/** Opening: away from the tile at once, then a long, soft settle into place (an ease-out-expo), no jolt at the end. */
private val FlyOpen = CubicBezierEasing(0.16f, 1f, 0.3f, 1f)
/** Closing: lifts off gently and glides into its tile (the curve Apple's sheets use). */
private val FlyHome = CubicBezierEasing(0.32f, 0.72f, 0f, 1f)

/** The app's shared-transition scope (MainActivity), so a tile and the page can share their photo. */
@OptIn(ExperimentalSharedTransitionApi::class)
val LocalSharedScope = staticCompositionLocalOf<SharedTransitionScope?> { null }

/** What the page shows, set where a photo was tapped ([PhotoSheet]) and shown over the whole app by [PhotoPageHost]. */
internal class PhotoPageRequest(
    val photo: Photo, val topic: String?, val heading: String?, val favorite: Boolean?,
    val onFavorite: ((Boolean) -> Unit)?, val onDelete: (() -> Unit)?, val onDismiss: () -> Unit,
    /** The picture the tile showed (already loaded): the page shows it at once, then the sharp one over it. */
    val tileUrl: String?,
    /** Swiping to the next (+1) or previous (-1) photo of the list it came from: that photo (null at an end), and going there. */
    val peek: (Int) -> Photo? = { null },
    val onStep: (Int) -> Unit = {},
    /** Whether a photo of the list is a favorite (History), for the ones sliding in; null where there are none (Explore). */
    val favoriteOf: (String) -> Boolean? = { null },
    /** The [PhotoSheet] that asked, so only it can take the page away. */
    val owner: Any? = null,
)

internal object PhotoPages {
    var request by mutableStateOf<PhotoPageRequest?>(null)
    /** True while the page's photo flies back into its tile (the tile is the one shown then). */
    var leaving by mutableStateOf(false)

    /**
     * The photo the page opened with: its tile flies in and back. Once the page is swiped to another photo it's
     * spent (null): that one wasn't tapped, so the page just fades away when it closes.
     */
    var origin by mutableStateOf<String?>(null)

    /** The tile that was tapped: hidden until the page closes, swiped on or not (shown again mid-way, it played its
     *  flight home behind the page). */
    var opened by mutableStateOf<String?>(null)

    /** The tile hidden while the page is open (not while it closes). Read by the page and the tiles alike, from the
     *  same states, so both change in the same frame. */
    val showing: String? get() = if (leaving) null else opened
}

private fun photoKey(id: String) = "photo-$id"

/**
 * Lets a tile of [photo] fly into its page when it's opened, and receive it back. Put it on the tile before its clip.
 */
@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun Modifier.sharedPhoto(photo: Photo): Modifier {
    val shared = LocalSharedScope.current ?: return this
    return with(shared) {
        this@sharedPhoto.sharedElementWithCallerManagedVisibility(
            rememberSharedContentState(photoKey(photo.id)), visible = PhotoPages.showing != photo.id,
        )
    }
}

/**
 * A photo opened on its own page (History passes [favorite] and the delete action; Explore and shared links don't).
 * The page itself is drawn by [PhotoPageHost], over everything, so the tile's photo can fly into it.
 */
@Composable
fun PhotoSheet(
    photo: Photo,
    topic: String?,
    onDismiss: () -> Unit,
    heading: String? = null,
    favorite: Boolean? = null,
    onFavorite: ((Boolean) -> Unit)? = null,
    onDelete: (() -> Unit)? = null,
    tileUrl: String? = null,
    peek: (Int) -> Photo? = { null },
    onStep: (Int) -> Unit = {},
    favoriteOf: (String) -> Boolean? = { null },
) {
    val owner = remember { Any() }
    val request = PhotoPageRequest(photo, topic, heading, favorite, onFavorite, onDelete, onDismiss, tileUrl, peek, onStep, favoriteOf, owner)
    SideEffect {
        val before = PhotoPages.request
        if (before == null) { PhotoPages.origin = photo.id; PhotoPages.opened = photo.id }
        else if (before.photo.id != photo.id) PhotoPages.origin = null   // swiped on: the tapped tile is no longer the one
        PhotoPages.request = request
    }
    // Swiping to another photo changes [photo] but keeps this sheet: only leaving the screen takes the page away.
    DisposableEffect(Unit) {
        onDispose {
            if (PhotoPages.request?.owner === owner) {
                PhotoPages.request = null
                PhotoPages.opened = null
            }
        }
    }
}

/**
 * The photo page over the whole app: the tapped tile's photo flies here and grows into Home's phone-shaped preview,
 * the actions rise in under it; back sends the photo flying home into its tile. Only positions and sizes move.
 */
@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun PhotoPageHost() {
    val shared = LocalSharedScope.current ?: return
    val request = PhotoPages.request
    // The last request stays while the page flies away, so there is something to show leaving.
    var shown by remember { mutableStateOf<PhotoPageRequest?>(null) }
    if (request != null) shown = request
    val leaving = PhotoPages.leaving
    val visible = request != null && !leaving

    BackHandler(visible) { PhotoPages.leaving = true }
    LaunchedEffect(leaving) {
        if (!leaving) return@LaunchedEffect
        delay(FLY_HOME.toLong() + 80)   // a moment past the landing
        // All at once, in one frame: the page never shows again for a frame after its photo landed.
        PhotoPages.request = null
        PhotoPages.opened = null
        PhotoPages.leaving = false
        shown?.onDismiss?.invoke()
    }

    // The page's paper eases in under the flying photo and eases out as it flies home (the photo itself is drawn above,
    // in the transition's overlay, so it never fades; the buttons slide).
    AnimatedVisibility(
        visible,
        enter = fadeIn(tween(320, easing = FlyOpen)),
        exit = fadeOut(tween(FLY_HOME - 60, delayMillis = 60, easing = FlyHome)),
    ) {
        shown?.let { PhotoPage(it, shared, this) { PhotoPages.leaving = true } }
    }
}

@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
private fun PhotoPage(r: PhotoPageRequest, shared: SharedTransitionScope, visibility: AnimatedVisibilityScope, onBack: () -> Unit) {
    val context = LocalContext.current
    val snackbar = LocalSnackbar.current
    val haptics = LocalHapticFeedback.current
    val busy by Oriel.wallpapers.busy.collectAsState()
    val phase by Oriel.wallpapers.phase.collectAsState()
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    val c = Palette.colors
    val photo = r.photo

    val swipe = remember { PhotoSwipe() }
    // A finished swipe left the pictures one step along: once this page shows the new photo, that's its rest (the
    // offset is read against the photo shown, so there's no frame where the old one is back in the middle).
    LaunchedEffect(photo.id) { swipe.settle(photo.id) }
    // The actions rise in from the bottom, and go back down.
    val rise = with(visibility) {
        Modifier.animateEnterExit(
            enter = slideInVertically(tween(FLY, easing = FlyOpen)) { it * 2 },
            exit = slideOutVertically(tween(FLY_HOME, easing = FlyHome)) { it * 2 },
        )
    }
    // Taps on the page stay on the page (not on the tabs under it).
    Box(Modifier.fillMaxSize().pointerInput(Unit) { detectTapGestures { } }) {
        BoxWithConstraints(Modifier.fillMaxSize()) {
        // Upright: the photo fills the screen, edge to edge, the controls over it in white (soft shades behind them).
        // On its side: on paper, the photo on the left as tall as the screen allows, a column on the right with the
        // back row at its top and the actions at its foot.
        val full = maxWidth <= maxHeight
        // Over the photo, the status bar's icons are light.
        val view = androidx.compose.ui.platform.LocalView.current
        DisposableEffect(full) {
            val bars = (view.context as? android.app.Activity)?.window?.let { androidx.core.view.WindowCompat.getInsetsController(it, view) }
            val before = bars?.isAppearanceLightStatusBars
            if (full) bars?.isAppearanceLightStatusBars = false
            onDispose { if (before != null) bars.isAppearanceLightStatusBars = before }
        }
        val ink = if (full) Color.White else c.ink
        // Over the photo the buttons are frosted glass: the photo behind them blurred, a light veil on it.
        val pageHaze = remember { HazeState() }
        val glass = if (full) Color.White.copy(alpha = 0.1f) else c.surface
        val frost = if (full) Modifier.clip(CircleShape).hazeEffect(pageHaze,
            HazeStyle(backgroundColor = Color.Black, tint = HazeTint(Color.White.copy(alpha = 0.06f)), blurRadius = 18.dp, noiseFactor = 0f)) else Modifier
        // Over the photo: back on the left, share on the right, nothing else (the photo says the rest).
        val header: @Composable (Modifier) -> Unit = { m ->
            Row(m.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                RoundIconButton(OrielIcons.ArrowLeft, "Back", onBack, frost, size = 44.dp, tint = ink, background = glass)
                Spacer(Modifier.weight(1f))
                RoundIconButton(OrielIcons.Share, "Share this wallpaper", modifier = frost, size = 44.dp, tint = ink, background = glass, onClick = {
                    Oriel.scope.launch(Dispatchers.Main) {
                        try {
                            sharePhoto(context, photo, "A wallpaper for you: ${uz.akbarjon.oriel.core.Sharing.shareLink(photo)}", "Share this wallpaper")
                        } catch (e: uz.akbarjon.oriel.data.OrielException) {
                            snackbar.showSnackbar(e.message ?: "Couldn't share")
                        }
                    }
                })
            }
        }
        // The credit Unsplash asks for: a quiet line by the actions (the photo itself carries no badge).
        val credit: @Composable (Modifier) -> Unit = { m ->
            if (!photo.isAi) Credit(photo, context, m, color = if (full) Color.White.copy(alpha = 0.85f) else c.muted)
        }
        val preview: @Composable (Modifier) -> Unit = { m ->
            // The tile's photo lands here, as big as the page allows (upright, the whole screen). Like a feed, it scrolls
            // up and down through the list it came from: the pictures slide, the next rising from below with the finger,
            // the previous coming down from above.
            BoxWithConstraints(
                m.photoSwipes(swipe, r),
                contentAlignment = Alignment.Center,
            ) {
                // Only the photo that was tapped flies (in, and back home); one reached by swiping just fades.
                val flight = if (PhotoPages.origin == photo.id) with(shared) {
                    Modifier.sharedElement(
                        rememberSharedContentState(photoKey(photo.id)), visibility,
                        // Out with a soft settle; home with a gentle lift-off and glide.
                        boundsTransform = { _, _ -> if (PhotoPages.leaving) tween(FLY_HOME, easing = FlyHome) else tween(FLY, easing = FlyOpen) },
                    )
                } else Modifier
                // Its final size, known before the first frame (the biggest phone shape that fits), so the flight aims
                // at the right place from the start and the sharp picture is asked for once.
                val density = LocalDensity.current
                val (fw, fh) = if (full) constraints.maxWidth to constraints.maxHeight else {
                    val aspect = screenW.toFloat() / screenH
                    val maxW = constraints.maxWidth
                    val maxH = constraints.maxHeight
                    if (maxW.toFloat() / maxH > aspect) (maxH * aspect).roundToInt() to maxH else maxW to (maxW / aspect).roundToInt()
                }
                val photoSize = Modifier.size(with(density) { fw.toDp() }, with(density) { fh.toDp() })
                SideEffect { swipe.height = fh.toFloat() }
                Box(
                    photoSize.then(flight)
                        // Laid out once at the final size and only scaled while it flies: the picture grows instead of
                        // re-flowing. One scale for both sides (a square tile crops, never squashes).
                        .clip(if (full) androidx.compose.ui.graphics.RectangleShape else RoundedCornerShape(11))
                        .layout { measurable, constraints ->
                            val placeable = measurable.measure(Constraints.fixed(fw, fh))
                            val w = constraints.maxWidth
                            val h = constraints.maxHeight
                            val k = maxOf(w / fw.toFloat(), h / fh.toFloat())
                            layout(w, h) {
                                placeable.placeWithLayer(((w - fw * k) / 2).roundToInt(), ((h - fh * k) / 2).roundToInt()) {
                                    transformOrigin = TransformOrigin(0f, 0f)
                                    scaleX = k
                                    scaleY = k
                                }
                            }
                        },
                ) {
                    WallpaperPreview(photo, screenW, screenH, Modifier.fillMaxSize(), credit = false, under = r.tileUrl, widthPx = fw,
                        // Only History's photos can be favorited: Explore and search photos take no double tap.
                        // A tick on every like, like Home's (it stays a favorite: liking never un-likes).
                        onLike = r.onFavorite?.let { favorite ->
                            {
                                haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                                if (r.favorite == false) favorite(true)
                            }
                        },
                        feed = PreviewFeed(r.peek(-1), r.peek(1)) { swipe.offset(photo.id) }, framed = !full)
                }
            }
        }
        val actions: @Composable (Modifier) -> Unit = { m ->
            Row(m, horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                // Already the wallpaper: nothing to set, just said so (not while a change is under way).
                if (!busy && currentPhoto()?.id == photo.id) {
                    Row(
                        Modifier.weight(1f).height(56.dp).clip(CircleShape).background(glass),
                        horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Icon(OrielIcons.Check, null, Modifier.size(18.dp), tint = ink)
                        Spacer(Modifier.width(10.dp))
                        Text("Your wallpaper", style = MaterialTheme.typography.labelLarge, color = ink)
                    }
                } else PrimaryButton(if (busy) phase?.text ?: "Setting…" else "Set as wallpaper", { setAsWallpaper(photo, r.topic, snackbar, onBack) },
                    Modifier.weight(1f), busy = busy, icon = { Icon(OrielIcons.Check, null, Modifier.size(20.dp)) },
                    container = if (full) Color.White else c.ink, content = if (full) Color(0xFF111114) else c.paper)
                if (r.favorite != null && r.onFavorite != null) {
                    val favorite = r.favorite
                    RoundIconButton(
                        if (favorite) OrielIcons.StarFilled else OrielIcons.Star,
                        if (favorite) "Remove from favorites" else "Add to favorites",
                        onClick = { haptics.performHapticFeedback(HapticFeedbackType.Confirm); r.onFavorite.invoke(!favorite) },
                        tint = if (favorite && full) StarGold else ink, background = glass,
                    )
                }
                r.onDelete?.let { delete ->
                    RoundIconButton(OrielIcons.Trash, "Delete from history", delete, background = glass,
                        tint = if (full) Color(0xFFFF8A80) else MaterialTheme.colorScheme.error)
                }
            }
        }
        if (full) {
            // The photo fills the app (between the status bar and the system's navigation), and over it, like a short
            // video: back and share on top; on the right the likes (and History's delete); bottom left what it is and
            // whose; the main action across the foot. While the photo flies in or home it's drawn above everything, so
            // the controls are too (they'd show under it, then jump on top); and they only come once it has landed,
            // fading up, and go at once when it leaves.
            // Drawn in order there too: the shades lowest, then the info and the main action, back and share on top.
            fun above(z: Float) = with(shared) { Modifier.renderInSharedTransitionScopeOverlay(zIndexInOverlay = z) }
            // They come in as the photo is almost full size (its curve is nearly there a little after a third of the
            // way): back and share down from the top, the words and the main action up from the bottom, the buttons on
            // the right in from the right. Leaving, they just fade, quickly.
            val start = 200
            fun comeIn(slide: androidx.compose.animation.EnterTransition) = with(visibility) {
                Modifier.animateEnterExit(enter = slide + fadeIn(tween(320, start, FlyOpen)), exit = fadeOut(tween(110)))
            }
            val fromTop = comeIn(slideInVertically(tween(440, start, FlyOpen)) { -it * 2 })
            val fromBottom = comeIn(slideInVertically(tween(440, start, FlyOpen)) { it * 2 })
            val fromRight = comeIn(androidx.compose.animation.slideInHorizontally(tween(440, start, FlyOpen)) { it * 2 })
            val settle = with(visibility) {
                Modifier.animateEnterExit(enter = fadeIn(tween(360, start, FlyOpen)), exit = fadeOut(tween(110)))
            }
            Box(Modifier.fillMaxSize().background(Color.Black).systemBarsPadding()) {
                preview(Modifier.fillMaxSize().hazeSource(pageHaze))
                // Soft shades at the top and the bottom, so the white controls read on any photo.
                Box(above(1f).then(settle).fillMaxWidth().height(140.dp).background(androidx.compose.ui.graphics.Brush.verticalGradient(
                    listOf(Color.Black.copy(alpha = 0.4f), Color.Transparent))))
                Box(above(1f).then(settle).align(Alignment.BottomCenter).fillMaxWidth().height(320.dp).background(androidx.compose.ui.graphics.Brush.verticalGradient(
                    listOf(Color.Transparent, Color.Black.copy(alpha = 0.65f)))))
                // Each photo's own info and buttons ride on it: they slide with the swipe, the next one's rising in with
                // its photo (keyed per photo, like the pictures). The main action stays at the foot.
                Box(above(2f).fillMaxSize().clipToBounds()) {
                    val column = listOfNotNull(r.peek(-1)?.let { -1 to it }, 0 to photo, r.peek(1)?.let { 1 to it })
                    for ((slot, p) in column) {
                        androidx.compose.runtime.key(p.id) {
                            val here = slot == 0
                            Row(
                                Modifier.align(Alignment.BottomCenter).padding(start = 16.dp, end = 16.dp, bottom = 16.dp + 56.dp + 16.dp)
                                    .graphicsLayer { translationY = swipe.offset(photo.id) + slot * swipe.height },
                                verticalAlignment = Alignment.Bottom,
                            ) {
                                // What it is and whose.
                                Column(Modifier.weight(1f).then(fromBottom).padding(start = 4.dp, end = 12.dp)) {
                                    p.description?.takeIf { it.isNotBlank() }?.let { words ->
                                        Text(words.replaceFirstChar { it.uppercase() }, style = MaterialTheme.typography.bodyMedium, color = Color.White,
                                            maxLines = 2, overflow = TextOverflow.Ellipsis)
                                        Spacer(Modifier.height(6.dp))
                                    }
                                    if (!p.isAi) Credit(p, context, color = Color.White.copy(alpha = 0.85f))
                                }
                                // Favorite (with the photo's count on Unsplash), and History's delete, stacked on the right.
                                Column(fromRight, horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(14.dp)) {
                                    val favorite = if (here) r.favorite else r.favoriteOf(p.id)
                                    val onFavorite = r.onFavorite.takeIf { here }
                                    SideAction(
                                        frost,
                                        if (favorite == true) OrielIcons.StarFilled else OrielIcons.Star,
                                        if (favorite == true) StarGold else Color.White,
                                        p.likes.takeIf { it > 0 }?.let { compactCount(it) } ?: "Favorite",
                                        if (favorite == null) "Favorited ${p.likes} times on Unsplash" else if (favorite) "Remove from favorites" else "Add to favorites",
                                        onClick = if (favorite != null && onFavorite != null) ({
                                            haptics.performHapticFeedback(HapticFeedbackType.Confirm); onFavorite(!favorite)
                                        }) else null,
                                    )
                                    r.onDelete?.let { delete ->
                                        SideAction(frost, OrielIcons.Trash, Color.White, "Delete", "Delete from history", onClick = delete.takeIf { here })
                                    }
                                }
                            }
                        }
                    }
                }
                Column(above(3f).then(fromBottom).align(Alignment.BottomCenter).padding(horizontal = 16.dp, vertical = 16.dp)) {
                    // The main action across the foot.
                    if (!busy && currentPhoto()?.id == photo.id) {
                        Row(
                            Modifier.fillMaxWidth().height(56.dp).then(frost).clip(CircleShape).background(glass),
                            horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Icon(OrielIcons.Check, null, Modifier.size(18.dp), tint = Color.White)
                            Spacer(Modifier.width(10.dp))
                            Text("Your wallpaper", style = MaterialTheme.typography.labelLarge, color = Color.White)
                        }
                    } else PrimaryButton(if (busy) phase?.text ?: "Setting…" else "Set as wallpaper", { setAsWallpaper(photo, r.topic, snackbar, onBack) },
                        Modifier.fillMaxWidth(), busy = busy, icon = { Icon(OrielIcons.Check, null, Modifier.size(20.dp)) },
                        container = Color.White, content = Color(0xFF111114))
                }
                // Back and share last, over everything.
                header(above(4f).then(fromTop).displayCutoutPadding().padding(horizontal = 16.dp, vertical = 12.dp))
            }
        } else {
            Row(Modifier.fillMaxSize().background(c.paper).systemBarsPadding().displayCutoutPadding().padding(horizontal = 20.dp, vertical = 12.dp)) {
                preview(Modifier.weight(1f).fillMaxHeight())
                Spacer(Modifier.width(28.dp))
                Column(Modifier.width(360.dp).fillMaxHeight()) {
                    header(Modifier)
                    Spacer(Modifier.weight(1f))
                    credit(Modifier.padding(start = 4.dp, bottom = 12.dp))
                    actions(rise)
                }
            }
        }
        }
    }
}

/** One of the photo page's actions on the right, like a short video's: a round frosted button, its count or name under it. */
@Composable
private fun SideAction(frost: Modifier, icon: ImageVector, tint: Color, label: String, description: String, onClick: (() -> Unit)?) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            Modifier.size(48.dp).then(frost).clip(CircleShape).background(Color.White.copy(alpha = 0.1f))
                .let { m -> if (onClick != null) m.springClick(pressed = 0.9f, onClick = onClick) else m }
                .semantics { contentDescription = description },
            contentAlignment = Alignment.Center,
        ) { Icon(icon, null, Modifier.size(24.dp), tint = tint) }
        Spacer(Modifier.height(4.dp))
        Text(label, style = MaterialTheme.typography.labelMedium, color = Color.White)
    }
}

/** A count the way people read it at a glance: 812, 1.2K, 34K, 1.5M. */
private fun compactCount(n: Int): String = when {
    n < 1_000 -> "%,d".format(n)
    n < 10_000 -> String.format(java.util.Locale.US, "%.1fK", n / 1000.0).replace(".0K", "K")
    n < 1_000_000 -> "${n / 1000}K"
    else -> String.format(java.util.Locale.US, "%.1fM", n / 1_000_000.0).replace(".0M", "M")
}

/**
 * How far the pictures on the page have been dragged up (to the next) or down (to the previous). A finished swipe
 * leaves them one step along and marks which photo it left ([leftFrom]); [offset] reads that against the photo the
 * page shows, so the moment the page shows the new one it sits at rest, whichever frame that lands in.
 */
private class PhotoSwipe {
    var y by mutableFloatStateOf(0f)
    /** The frame's height: one step. */
    var height by mutableFloatStateOf(1f)
    private var leftFrom by mutableStateOf<String?>(null)
    private var leftDir = 0
    private var motion: Job? = null

    /** Where the pictures are for the page showing [shownId]. */
    fun offset(shownId: String): Float = if (leftFrom != null && leftFrom != shownId) y + leftDir * height else y

    /** The page shows [shownId]: a step left behind becomes its rest (nothing moves). */
    fun settle(shownId: String) {
        if (leftFrom == null || leftFrom == shownId) return
        y += leftDir * height
        leftFrom = null
    }

    fun stop() { motion?.cancel() }

    /** Glides [y] to [target], then runs [then]. */
    fun glide(scope: CoroutineScope, target: Float, spec: AnimationSpec<Float>, then: () -> Unit = {}) {
        motion?.cancel()
        motion = scope.launch {
            animate(y, target, animationSpec = spec) { value, _ -> y = value }
            then()
        }
    }

    /** Swiped one step on from [fromId] ([dir] +1 the next, -1 the previous): the pictures stay where they are. */
    fun stepped(fromId: String, dir: Int) {
        leftFrom = fromId
        leftDir = dir
    }
}

/**
 * The photo page's swipes, like scrolling a feed: up goes to the next photo, down to the previous; the pictures follow
 * the finger and the move completes past a fifth of the frame or with a flick, else springs back. Past either end of
 * the list they only give a little and spring back. A light tick marks each move.
 */
private fun Modifier.photoSwipes(swipe: PhotoSwipe, r: PhotoPageRequest): Modifier = composed {
    val scope = rememberCoroutineScope()
    val haptics = LocalHapticFeedback.current
    val request = rememberUpdatedState(r)
    pointerInput(Unit) {
        val fling = 1000.dp.toPx()
        val tracker = VelocityTracker()
        detectVerticalDragGestures(
            onDragStart = {
                swipe.stop()
                swipe.settle(request.value.photo.id)
                tracker.resetTracking()
            },
            onDragCancel = { swipe.glide(scope, 0f, spring()) },
            onDragEnd = {
                val v = tracker.calculateVelocity().y
                val h = swipe.height
                val dy = swipe.y
                val dir = if (dy < 0) 1 else -1   // up: the next one; down: the previous one
                val far = dy != 0f && (abs(dy) > h / 5 || (abs(v) > fling && (v < 0) == (dy < 0)))
                if (far && request.value.peek(dir) != null) {
                    haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
                    val from = request.value.photo.id
                    swipe.glide(scope, -dir * h, tween(280, easing = Motion.Emphasized)) {
                        // The neighbor fills the frame now: it becomes the page's photo, the pictures staying put.
                        swipe.stepped(from, dir)
                        request.value.onStep(dir)
                    }
                } else swipe.glide(scope, 0f, spring(dampingRatio = 0.82f, stiffness = 420f))
            },
        ) { change, drag ->
            change.consume()
            tracker.addPosition(change.uptimeMillis, change.position)
            val next = swipe.y + drag
            // Toward a photo it follows the finger; past either end of the list it only gives a little.
            val toward = if (next < 0) 1 else -1
            swipe.y = if (request.value.peek(toward) != null) next else swipe.y + drag * 0.3f
        }
    }
}
