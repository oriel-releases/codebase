package uz.akbarjon.oriel.ui

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

/** Sets [photo] as the wallpaper and says how it went. Keeps going if the screen that asked goes away. */
fun setAsWallpaper(photo: Photo, topic: String?, snackbar: SnackbarHostState, onDone: () -> Unit = {}) {
    Oriel.scope.launch(Dispatchers.Main) {
        val result = Oriel.wallpapers.apply(photo, topic)
        if (result is ChangeResult.Applied) onDone()
        snackbar.showSnackbar(
            when (result) {
                is ChangeResult.Applied -> "Wallpaper set"
                is ChangeResult.Failed -> result.message
                ChangeResult.Skipped -> "Nothing changed"
            },
        )
    }
}

// ---- The photo page, and the flight there from the tile that was tapped (like Windows' connected animation) ----

/** How long the photo takes to fly between its tile and its page, both ways. */
private const val FLY = 440

/** The app's shared-transition scope (MainActivity), so a tile and the page can share their photo. */
@OptIn(ExperimentalSharedTransitionApi::class)
val LocalSharedScope = staticCompositionLocalOf<SharedTransitionScope?> { null }

/** What the page shows, set where a photo was tapped ([PhotoSheet]) and shown over the whole app by [PhotoPageHost]. */
internal class PhotoPageRequest(
    val photo: Photo, val topic: String?, val heading: String?, val favorite: Boolean?,
    val onFavorite: ((Boolean) -> Unit)?, val onDelete: (() -> Unit)?, val onDismiss: () -> Unit,
    /** The picture the tile showed (already loaded): the page shows it at once, then the sharp one over it. */
    val tileUrl: String?,
)

internal object PhotoPages {
    var request by mutableStateOf<PhotoPageRequest?>(null)
    /** True while the page's photo flies back into its tile (the tile is the one shown then). */
    var leaving by mutableStateOf(false)

    /** The photo the page is showing (not while it flies back): its tile hides meanwhile. Read by the page and the tiles
     *  alike, from the same two states, so both change in the same frame. */
    val showing: String? get() = if (leaving) null else request?.photo?.id
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
) {
    val request = PhotoPageRequest(photo, topic, heading, favorite, onFavorite, onDelete, onDismiss, tileUrl)
    SideEffect { PhotoPages.request = request }
    DisposableEffect(photo.id) {
        onDispose { if (PhotoPages.request?.photo?.id == photo.id) PhotoPages.request = null }
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
        delay(FLY.toLong())
        // All at once, in one frame: the page never shows again for a frame after its photo landed.
        PhotoPages.request = null
        PhotoPages.leaving = false
        shown?.onDismiss?.invoke()
    }

    // The page's paper eases in under the flying photo and eases out as it flies home (the photo itself is drawn above,
    // in the transition's overlay, so it never fades; the buttons slide).
    AnimatedVisibility(
        visible,
        enter = fadeIn(tween(260, easing = Motion.Emphasized)),
        exit = fadeOut(tween(FLY - 80, delayMillis = 80, easing = Motion.Emphasized)),
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

    // Taps on the page stay on the page (not on the tabs under it).
    Box(Modifier.fillMaxSize().background(c.paper).pointerInput(Unit) { detectTapGestures { } }) {
        Column(Modifier.fillMaxSize().systemBarsPadding().padding(horizontal = 20.dp, vertical = 12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                RoundIconButton(OrielIcons.ArrowLeft, "Back", onBack, size = 44.dp)
                Spacer(Modifier.width(14.dp))
                Text(r.heading ?: "Wallpaper", style = MaterialTheme.typography.titleMedium, maxLines = 1,
                    overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            }
            Spacer(Modifier.height(16.dp))
            // The tile's photo lands here, in Home's phone-shaped preview, as big as the page allows.
            BoxWithConstraints(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                val flight = with(shared) {
                    Modifier.sharedElement(
                        rememberSharedContentState(photoKey(photo.id)), visibility,
                        boundsTransform = { _, _ -> tween(FLY, easing = Motion.Emphasized) },
                    )
                }
                // Its final size, known before the first frame (the biggest phone shape that fits), so the flight aims
                // at the right place from the start and the sharp picture is asked for once.
                val density = LocalDensity.current
                val aspect = screenW.toFloat() / screenH
                val (fw, fh) = with(density) {
                    val maxW = constraints.maxWidth
                    val maxH = constraints.maxHeight
                    if (maxW.toFloat() / maxH > aspect) (maxH * aspect).roundToInt() to maxH else maxW to (maxW / aspect).roundToInt()
                }
                Box(
                    Modifier.size(with(density) { fw.toDp() }, with(density) { fh.toDp() }).then(flight)
                        // Laid out once at the final size and only scaled while it flies: the frame, corners and credit
                        // grow together instead of re-flowing. One scale for both sides (a square tile crops, never squashes).
                        .clip(RoundedCornerShape(11))
                        .layout { measurable, constraints ->
                            val placeable = measurable.measure(Constraints.fixed(fw, fh))
                            val w = constraints.maxWidth
                            val h = constraints.maxHeight
                            val s = maxOf(w / fw.toFloat(), h / fh.toFloat())
                            layout(w, h) {
                                placeable.placeWithLayer(((w - fw * s) / 2).roundToInt(), ((h - fh * s) / 2).roundToInt()) {
                                    transformOrigin = TransformOrigin(0f, 0f)
                                    scaleX = s
                                    scaleY = s
                                }
                            }
                        },
                ) {
                    WallpaperPreview(photo, screenW, screenH, Modifier.fillMaxSize(), under = r.tileUrl, widthPx = fw,
                        onLike = { if (r.favorite == false) r.onFavorite?.invoke(true) })
                }
            }
            Spacer(Modifier.height(20.dp))
            // The actions rise in from the bottom, and go back down.
            val rise = with(visibility) {
                Modifier.animateEnterExit(
                    enter = slideInVertically(tween(FLY, easing = Motion.Decelerate)) { it * 2 },
                    exit = slideOutVertically(tween(FLY, easing = Motion.Emphasized)) { it * 2 },
                )
            }
            Row(rise, horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                PrimaryButton(if (busy) phase?.text ?: "Setting…" else "Set as wallpaper", { setAsWallpaper(photo, r.topic, snackbar, onBack) },
                    Modifier.weight(1f), busy = busy, icon = { Icon(OrielIcons.Check, null, Modifier.size(20.dp)) })
                if (r.favorite != null && r.onFavorite != null) {
                    val favorite = r.favorite
                    RoundIconButton(
                        if (favorite) OrielIcons.StarFilled else OrielIcons.Star,
                        if (favorite) "Remove from favorites" else "Add to favorites",
                        onClick = { haptics.performHapticFeedback(HapticFeedbackType.Confirm); r.onFavorite.invoke(!favorite) },
                    )
                }
                RoundIconButton(OrielIcons.Share, "Share this wallpaper", {
                    Oriel.scope.launch(Dispatchers.Main) {
                        try {
                            shareText(context, "A wallpaper for you: ${uz.akbarjon.oriel.core.Sharing.shareLink(photo)}", "Share this wallpaper")
                        } catch (e: uz.akbarjon.oriel.data.OrielException) {
                            snackbar.showSnackbar(e.message ?: "Couldn't share")
                        }
                    }
                })
                r.onDelete?.let { delete ->
                    RoundIconButton(OrielIcons.Trash, "Delete from history", delete, tint = MaterialTheme.colorScheme.error)
                }
            }
        }
    }
}
