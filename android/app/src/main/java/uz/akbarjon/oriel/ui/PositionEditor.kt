package uz.akbarjon.oriel.ui

import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.displayCutoutPadding
import kotlin.math.roundToInt
import kotlinx.coroutines.withContext
import android.graphics.Bitmap
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.RoundRect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathFillType
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import android.view.View
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.snapshotFlow
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.lerp
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.LayoutCoordinates
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.util.lerp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import kotlin.math.abs
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.ChangeResult
import uz.akbarjon.oriel.core.Positions
import uz.akbarjon.oriel.data.Photo
import uz.akbarjon.oriel.data.Position

private val White = Color.White
private val Ink = Color(0xFF111114)

/** How long the window rests before its crop starts downloading: past a tap's settle, short of reaching for Set. */
private const val SETTLE_MS = 300L

/** Which crop the editor is on: Unsplash's smart crop, the middle, or the window the user placed. */
private enum class Mode { Smart, Center, Custom }

/**
 * Where the editor opens out of and closes back into: Home's phone frame, on screen. While the editor's copy of the
 * photo covers it, the frame hides ([covered]): one photo moves, no copy is left behind.
 */
class EditorOrigin {
    var bounds by mutableStateOf<Rect?>(null)
    var covered by mutableStateOf(false)
    /** Home's picture of the photo cropped at a position: loaded before the editor folds back, so it lands on it. */
    var pictureFor: ((Position) -> String)? = null
}

/** The editor's picture of the whole photo: one, at a modest size (the window shows its part; Set downloads the real crop). */
private fun editorPicture(photo: Photo) = photo.sized(1400, quality = 85, format = "webp")

/** Where Unsplash's smart crop falls on each photo, found once (it takes a few small downloads). */
private val smartFound = java.util.concurrent.ConcurrentHashMap<String, Float>()

/**
 * Where the smart crop falls on [photo] (0..1 along it), found once; the middle when it can't be found right now.
 * [homePicture]: Home's own picture of it with the smart crop, so the window opens on exactly what Home shows.
 */
private suspend fun smartAtFor(photo: Photo, homePicture: String?): Float {
    smartFound[photo.id]?.let { return it }
    if (photo.isAi) return 0.5f
    val (w, h) = Oriel.wallpapers.screenSize()
    val found = smartCenter(photo, w.toFloat() / h, homePicture) ?: return 0.5f   // not kept: it may work next time
    smartFound[photo.id] = found
    return found
}

/**
 * Loads what the editor needs for [photo] ahead of time (Home does, as each photo shows): the whole photo, small, and
 * where its smart crop falls. Then Crop opens at once, with nothing to wait for. Unsplash's CDN, not its API: no requests
 * from the shared key are spent.
 */
suspend fun prepareEditor(photo: Photo, homePicture: String?) {
    loadBitmap(editorPicture(photo))
    smartAtFor(photo, homePicture)
}

/** These bounds on the screen: the app's window and the editor's (a dialog) each count from their own corner. */
fun LayoutCoordinates.boundsOnScreen(view: View): Rect {
    val at = IntArray(2).also(view::getLocationOnScreen)
    return boundsInRoot().translate(at[0].toFloat(), at[1].toFloat())
}

/** In: arrives already moving and settles slowly (the app's flights). Out: the same feel, a little quicker. */
private val Unfold = CubicBezierEasing(0.05f, 0.7f, 0.1f, 1f)
private val Fold = CubicBezierEasing(0.2f, 0f, 0f, 1f)

/**
 * Where the wallpaper sits on this phone. The whole photo is shown with a phone-shaped window over it; drag (or tap)
 * to move the window across a wide photo (or up and down a tall one). Outside it is dimmed, it snaps to the middle
 * with a tick, and the thirds show while dragging. Smart and Center are one tap away. Set crops it there (Unsplash's
 * CDN does the cropping, so it stays full quality) and remembers it for this photo. Only this phone's.
 *
 * Opened from Home ([origin]), it unfolds out of Home's photo: the window starts exactly over it, showing the same part
 * of the photo, glides to its place, and the rest of the photo comes in around it, dimmed, with the controls. Closing
 * folds it back into Home's photo. While the photo loads, Home stays as it is, with a small spinner on its photo.
 */
@Composable
fun PositionEditor(
    photo: Photo, onSet: (suspend (Position) -> ChangeResult)? = null, origin: EditorOrigin? = null, onDismiss: () -> Unit,
) {
    val snackbar = LocalSnackbar.current
    val haptics = LocalHapticFeedback.current
    val busy by Oriel.wallpapers.busy.collectAsState()
    val phase by Oriel.wallpapers.phase.collectAsState()
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    val screenAspect = screenW.toFloat() / screenH
    // Usually ready already (Home prepared it): then it opens at once.
    val bitmap by produceState(cachedBitmap(editorPicture(photo)), photo.id) { if (value == null) value = loadBitmap(editorPicture(photo)) }

    val current = remember(photo.id) { Positions.of(photo.id) }
    val start = current as? Position.Focus
    var mode by remember(photo.id) {
        mutableStateOf(when (current) { is Position.Focus -> Mode.Custom; Position.Center -> Mode.Center; else -> Mode.Smart })
    }
    // Where Unsplash's smart crop really falls on this photo (0..1 along it); null while looking (rare: Home prepares it).
    // The window waits for it, so it opens where it belongs instead of gliding there.
    val smartAt by produceState(smartFound[photo.id] ?: 0.5f.takeIf { photo.isAi }, photo.id) { if (value == null) value = smartAtFor(photo, origin?.pictureFor?.invoke(Position.Smart)) }
    // The window's center, 0..1 across (or down) the photo.
    var focus by remember(photo.id) { mutableFloatStateOf(start?.let { it.x } ?: 0.5f) }
    var focusY by remember(photo.id) { mutableFloatStateOf(start?.let { it.y } ?: 0.5f) }
    var dragging by remember { mutableStateOf(false) }

    // Where the window rests, the crop starts downloading (and Home's picture of it): mostly ready by the time Set is
    // tapped, instead of being asked for only then. Not while dragging, nor for where it already was.
    LaunchedEffect(mode, focus, focusY, dragging) {
        if (dragging) return@LaunchedEffect
        delay(SETTLE_MS)
        val position = when (mode) {
            Mode.Smart -> Position.Smart
            Mode.Center -> Position.Center
            Mode.Custom -> Position.Focus(focus, focusY)
        }
        if (onSet != null || position == current) return@LaunchedEffect
        Oriel.wallpapers.prefetchCrop(photo, position)
        origin?.pictureFor?.invoke(position)?.let { loadBitmap(it) }
    }
    DisposableEffect(Unit) { onDispose { Oriel.wallpapers.discardPrefetchedCrop() } }

    // 0: folded into Home's photo; 1: the editor, open.
    val reveal = remember { Animatable(0f) }
    val ready = bitmap != null && smartAt != null
    val scope = rememberCoroutineScope()
    var leaving by remember { mutableStateOf(false) }
    LaunchedEffect(ready) { if (ready && !leaving) reveal.animateTo(1f, tween(560, easing = Unfold)) }
    // Home's frame hides the moment the copy starts to move, and shows again the moment it has landed: the photo itself
    // never fades, there's always exactly one, solid.
    LaunchedEffect(origin) { snapshotFlow { reveal.value > 0f }.collect { origin?.covered = it } }
    DisposableEffect(origin) { onDispose { origin?.covered = false } }

    /**
     * Folds back into Home's photo, then goes. Home's photo is shown again first and the editor stays two frames more, so
     * Home has drawn it underneath before the editor's window goes: gone in the same frame, there was a frame with an
     * empty hole where the photo is. Not open yet (still loading): just goes.
     */
    fun close(kept: Boolean = false) {
        if (leaving) return
        leaving = true
        // Not set: Home still shows the photo where it was, so the window glides back there as it folds, and lands on
        // exactly what Home shows (it used to fold with the moved window, then jump). Set: Home has the new crop already.
        if (!kept) {
            mode = when (current) { is Position.Focus -> Mode.Custom; Position.Center -> Mode.Center; else -> Mode.Smart }
            focus = start?.x ?: 0.5f
            focusY = start?.y ?: 0.5f
            dragging = false
        }
        scope.launch {
            if (reveal.value > 0f) reveal.animateTo(0f, tween(420, easing = Fold))
            origin?.covered = false
            withFrameNanos { }
            withFrameNanos { }
            onDismiss()
        }
    }

    // Drawn over the app itself, not in a window of its own (a dialog): the system's back gesture animated that window
    // to a corner while the editor folded away, and it got stuck there, its last picture over every page. Back (the
    // button or the gesture) folds it away like the close button; touches stop here, not on the page under it.
    androidx.activity.compose.BackHandler(enabled = !busy) { close() }
    Box(Modifier.fillMaxSize().pointerInput(Unit) { detectTapGestures { } }) {
        val view = LocalView.current
        val density = LocalDensity.current
        var rootAt by remember { mutableStateOf(Offset.Zero) }
        // Home's photo, in this dialog's own coordinates.
        val from = origin?.bounds?.translate(-rootAt.x, -rootAt.y)
        Box(
            Modifier.fillMaxSize()
                .onGloballyPositioned { rootAt = it.boundsOnScreen(view).topLeft }
                // The dark comes in quicker than the photo moves, so Home is gone behind it early.
                .drawBehind { drawRect(Color(0xFF0B0B0D), alpha = (reveal.value * 2.2f).coerceAtMost(1f)) },
        ) {
            // Upright: title, the photo, then the controls under it. On its side the photo takes the height it needs,
            // and the title and controls stand beside it, in a column of their own.
            BoxWithConstraints(Modifier.fillMaxSize().systemBarsPadding().displayCutoutPadding().padding(20.dp)) {
                val wide = maxWidth > maxHeight
                @Composable
                fun TitleBar() {
                Row(
                    Modifier.fillMaxWidth().height(48.dp).graphicsLayer {
                        alpha = reveal.value
                        translationY = (1 - reveal.value) * -20.dp.toPx()
                    },
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text("Position", style = MaterialTheme.typography.titleLarge, color = White, modifier = Modifier.weight(1f))
                    Box(Modifier.size(40.dp).clip(CircleShape).background(White.copy(alpha = 0.12f)).springClick(!busy) { close() },
                        contentAlignment = Alignment.Center) { Icon(OrielIcons.Close, "Close", tint = White, modifier = Modifier.size(18.dp)) }
                }
                }
                @Composable
                fun PhotoArea(modifier: Modifier) {
                // The photo, whole, with the window over it.
                BoxWithConstraints(modifier, contentAlignment = Alignment.Center) {
                    val bmp = bitmap
                    if (bmp == null || smartAt == null) return@BoxWithConstraints
                    val photoAspect = bmp.width.toFloat() / bmp.height
                    // The photo fit into the space.
                    val boxAspect = maxWidth / maxHeight
                    val pw: Dp = if (photoAspect > boxAspect) maxWidth else maxHeight * photoAspect
                    val ph: Dp = if (photoAspect > boxAspect) maxWidth / photoAspect else maxHeight
                    // Wider than the screen: the window moves across. Taller: up and down. The same shape: nothing to move.
                    val across = photoAspect > screenAspect
                    val ww: Dp = if (across) ph * screenAspect else pw
                    val wh: Dp = if (across) ph else pw / screenAspect
                    val slack = if (across) pw - ww else ph - wh
                    val shown = when (mode) {
                        Mode.Custom -> if (across) focus else focusY
                        Mode.Smart -> smartAt ?: 0.5f   // where Unsplash finds faces or detail (an AI picture: the middle)
                        Mode.Center -> 0.5f
                    }
                    val center by animateFloatAsState(shown, if (dragging) spring(stiffness = 3000f) else spring(dampingRatio = 0.8f, stiffness = 500f), label = "window")
                    val length = if (across) pw else ph
                    val window = if (across) ww else wh
                    val offset: Dp = (length * center - window / 2).coerceIn(0.dp, slack.coerceAtLeast(0.dp))
                    val ox = if (across) offset else 0.dp
                    val oy = if (across) 0.dp else offset

                    /** Puts the window's center at [px] (clamped to the photo), snapping to the middle with a tick. */
                    fun place(px: Float) {
                        val len = with(density) { length.toPx() }
                        val half = with(density) { window.toPx() } / 2 / len
                        val raw = (px / len).coerceIn(half, (1 - half).coerceAtLeast(half))
                        val snapped = if (abs(raw - 0.5f) < 0.025f) 0.5f else raw
                        val before = if (across) focus else focusY
                        if (snapped == 0.5f && (before != 0.5f || mode != Mode.Custom)) haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
                        // A lighter tick crossing a third of the way, where the photo's thirds lines fall.
                        else if (mode == Mode.Custom && listOf(1f / 3, 2f / 3).any { (before - it) * (snapped - it) < 0f })
                            haptics.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
                        if (across) focus = snapped else focusY = snapped
                        mode = Mode.Custom
                    }

                    var photoAt by remember { mutableStateOf<Offset?>(null) }
                    Box(Modifier.size(pw, ph)
                        // Where it lies when open (measured outside the move below, which doesn't change it).
                        .onGloballyPositioned { photoAt = it.boundsOnScreen(view).topLeft - rootAt }
                        // The move: scaled and shifted so the window lies exactly on Home's photo at 0, in place at 1.
                        .graphicsLayer {
                            val e = reveal.value
                            val f = from
                            val at = photoAt
                            if (f == null || at == null) {
                                // Not opened from Home: it just grows in.
                                alpha = e
                                scaleX = 0.94f + 0.06f * e
                                scaleY = scaleX
                                return@graphicsLayer
                            }
                            transformOrigin = TransformOrigin(0f, 0f)
                            val windowAt = Offset(ox.toPx(), oy.toPx())
                            val s = lerp(f.width / ww.toPx(), 1f, e)
                            val topLeft = lerp(f.topLeft, at + windowAt, e)
                            scaleX = s
                            scaleY = s
                            translationX = topLeft.x - at.x - s * windowAt.x
                            translationY = topLeft.y - at.y - s * windowAt.y
                        }
                        // Not while a change is being set: the window stays where it was set.
                        .pointerInput(across, pw, ph, busy) {
                            if (busy) return@pointerInput
                            detectDragGestures(
                                onDragStart = {
                                    dragging = true
                                    haptics.performHapticFeedback(HapticFeedbackType.GestureThresholdActivate)
                                    place(if (across) it.x else it.y)
                                },
                                onDragEnd = { dragging = false },
                                onDragCancel = { dragging = false },
                            ) { change, _ -> place(if (across) change.position.x else change.position.y) }
                        }
                        .pointerInput(across, pw, ph, busy) { if (!busy) detectTapGestures { p -> place(if (across) p.x else p.y) } },
                    ) {
                        val dim = Color.Black.copy(alpha = 0.6f)
                        val corner = screenCorner(ww)
                        // The rest of the photo, dimmed outside the window (its rounded corners too). It doesn't fade: it
                        // unfolds, its visible part growing out from the window to the whole photo (and folding back in).
                        Box(Modifier.fillMaxSize().drawWithContent {
                            val e = reveal.value
                            // The same corners as the window over it (its border and its hole), folded or open.
                            val r = corner.toPx()
                            val shown = Path().apply {
                                addRoundRect(RoundRect(
                                    lerp(ox.toPx(), 0f, e), lerp(oy.toPx(), 0f, e),
                                    lerp((ox + ww).toPx(), size.width, e), lerp((oy + wh).toPx(), size.height, e),
                                    CornerRadius(r, r),
                                ))
                            }
                            clipPath(shown) { this@drawWithContent.drawContent() }
                        }) {
                            Image(bmp, photo.description, Modifier.fillMaxSize(), contentScale = ContentScale.FillBounds)
                            Canvas(Modifier.fillMaxSize()) {
                                // The window's hole: the window's corners (in proportion to its width, like its border).
                                val r = corner.toPx()
                                val hole = Path().apply {
                                    fillType = PathFillType.EvenOdd
                                    addRect(Rect(Offset.Zero, size))
                                    addRoundRect(RoundRect(ox.toPx(), oy.toPx(), (ox + ww).toPx(), (oy + wh).toPx(), CornerRadius(r, r)))
                                }
                                drawPath(hole, dim)
                            }
                        }
                        // The part in the window, as Home shows it: what leaves Home's photo and comes back to it, solid
                        // all the way, rounded like the phone's screen (as Home's frame is).
                        Canvas(Modifier.offset(x = ox, y = oy).size(ww, wh)) {
                            val r = ScreenCorners.of(size.width)
                            val src = IntOffset((ox / pw * bmp.width).roundToInt(), (oy / ph * bmp.height).roundToInt())
                            val srcSize = IntSize(
                                (ww / pw * bmp.width).roundToInt().coerceAtMost(bmp.width - src.x),
                                (wh / ph * bmp.height).roundToInt().coerceAtMost(bmp.height - src.y),
                            )
                            clipPath(Path().apply { addRoundRect(RoundRect(0f, 0f, size.width, size.height, CornerRadius(r, r))) }) {
                                drawImage(bmp, src, srcSize, dstSize = IntSize(size.width.roundToInt(), size.height.roundToInt()))
                            }
                        }
                        // The window: the phone's shape, with the thirds while dragging.
                        Box(Modifier.offset(x = ox, y = oy).size(ww, wh).graphicsLayer { alpha = reveal.value }
                            .border(2.dp, White, ScreenShape)) {
                            val grid by animateFloatAsState(if (dragging) 1f else 0f, tween(if (dragging) 120 else 300), label = "grid")
                            if (grid > 0f) {
                                Canvas(Modifier.fillMaxSize()) {
                                    val line = White.copy(alpha = 0.45f * grid)
                                    for (i in 1..2) {
                                        drawLine(line, Offset(size.width * i / 3, 0f), Offset(size.width * i / 3, size.height), 1.5f)
                                        drawLine(line, Offset(0f, size.height * i / 3), Offset(size.width, size.height * i / 3), 1.5f)
                                    }
                                }
                            }
                        }
                    }
                }
                }
                @Composable
                fun Controls() {
                Column(
                    Modifier.fillMaxWidth().graphicsLayer {
                        alpha = reveal.value
                        translationY = (1 - reveal.value) * 28.dp.toPx()
                    },
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Spacer(Modifier.height(20.dp))
                    // Smart, Center, or where you put it: the same switch as History's All / Favorites, its pill sliding over.
                    Segmented(
                        listOf(Mode.Smart to "Smart", Mode.Center to "Center", Mode.Custom to "Custom"), mode,
                        { m ->
                            mode = m
                            if (m == Mode.Center) { focus = 0.5f; focusY = 0.5f }
                        },
                        Modifier.widthIn(max = 320.dp).fillMaxWidth(),
                        track = White.copy(alpha = 0.1f), pill = White, picked = Ink, others = White.copy(alpha = 0.75f), border = Color.Transparent,
                    )
                    Spacer(Modifier.height(16.dp))
                    PrimaryButton(
                        phase?.text ?: "Set",
                        onClick = {
                            val position = when (mode) {
                                Mode.Smart -> Position.Smart
                                Mode.Center -> Position.Center
                                Mode.Custom -> Position.Focus(focus, focusY)
                            }
                            // Nothing moved: nothing to download or set, it just folds back.
                            if (onSet == null && position == current) { close(); return@PrimaryButton }
                            Oriel.scope.launch(Dispatchers.Main) {
                                when (val result = onSet?.invoke(position) ?: Oriel.wallpapers.reposition(photo, position)) {
                                    // Done: a tick, Home's new picture is loaded (a moment at most), and it folds back onto it.
                                    is ChangeResult.Applied -> {
                                        haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                                        origin?.pictureFor?.invoke(position)?.let { url -> withTimeoutOrNull(1500) { loadBitmap(url) } }
                                        close(kept = true)
                                    }
                                    is ChangeResult.Failed -> snackbar.showSnackbar(result.message)
                                    ChangeResult.Skipped -> Unit
                                }
                            }
                        },
                        modifier = Modifier.fillMaxWidth(), busy = busy, container = White, content = Ink,
                        icon = { Icon(OrielIcons.Check, null, Modifier.size(18.dp)) },
                    )
                }
                }
                if (wide) Row(Modifier.fillMaxSize(), verticalAlignment = Alignment.CenterVertically) {
                    PhotoArea(Modifier.weight(1f).fillMaxHeight())
                    Spacer(Modifier.width(24.dp))
                    Column(Modifier.width(300.dp)) { TitleBar(); Controls() }
                } else Column(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally) {
                    TitleBar()
                    Spacer(Modifier.height(16.dp))
                    PhotoArea(Modifier.weight(1f).fillMaxWidth())
                    Controls()
                }
            }

            // Still loading: a small spinner on Home's photo (in the middle when opened from elsewhere).
            if (!ready) {
                val spot = from?.center
                Box(
                    Modifier.then(
                        if (spot != null) Modifier.offset { IntOffset((spot.x - 22.dp.toPx()).roundToInt(), (spot.y - 22.dp.toPx()).roundToInt()) }
                        else Modifier.align(Alignment.Center),
                    ).size(44.dp).clip(CircleShape).background(Color.Black.copy(alpha = 0.45f)),
                    contentAlignment = Alignment.Center,
                ) { CircularProgressIndicator(Modifier.size(22.dp), color = White, strokeWidth = 2.dp) }
            }
        }
    }
}

/**
 * Where Unsplash's smart crop (faces, then detail) puts the phone-shaped window on [photo]: 0..1 along the side it
 * moves on (across a wide photo, down a tall one), the window's middle. Unsplash doesn't say, so it's found by looking:
 * the smart-cropped picture and the whole photo, both small and the same height (or width), and the place along the
 * photo where the crop matches best. Null when either can't be had.
 */
private suspend fun smartCenter(photo: Photo, screenAspect: Float, homePicture: String?): Float? = withContext(Dispatchers.Default) {
    runCatching {
        // The photo's shape: known for a photo picked here; one from another device comes without it, so it's read off
        // the editor's own picture of it (loaded anyway). Without it the window opened in the middle, not where Home's is.
        val photoAspect = if (photo.width > 0 && photo.height > 0) photo.width.toFloat() / photo.height
            else loadBitmap(editorPicture(photo))?.let { it.width.toFloat() / it.height } ?: return@runCatching null
        val across = photoAspect > screenAspect
        // Both pictures with the same fixed side, so the crop slides along the other one. Fetched at the same time.
        val fixed = 160
        val (full, crop) = kotlinx.coroutines.coroutineScope {
            val full = async { decode(if (across) photo.sized((fixed * photoAspect).roundToInt(), quality = 80) else photo.sized(fixed, quality = 80)) }
            val crop = async {
                // Home's own picture when there is one: Unsplash's smart crop lands a little differently at each size, so
                // a separate small one could sit elsewhere than what Home shows. Brought to the same fixed side.
                val (cw, ch) = if (across) (fixed * screenAspect).roundToInt() to fixed else fixed to (fixed / screenAspect).roundToInt()
                homePicture?.let { url -> decode(url)?.let { Bitmap.createScaledBitmap(it, cw, ch, true) } }
                    ?: decode(photo.sized(cw, ch, quality = 80, position = Position.Smart))
            }
            full.await() to crop.await()
        }
        if (full == null || crop == null) return@runCatching null
        val length = if (across) full.width else full.height
        val window = if (across) crop.width else crop.height
        val side = minOf(if (across) full.height else full.width, if (across) crop.height else crop.width)
        if (window >= length) return@runCatching 0.5f
        // Brightness of every pixel, read in one go (a call per pixel made this the slow part of opening Crop).
        fun luma(b: Bitmap): IntArray {
            val px = IntArray(b.width * b.height)
            b.getPixels(px, 0, b.width, 0, 0, b.width, b.height)
            for (i in px.indices) px[i].let { px[i] = ((it shr 16 and 0xFF) * 3 + (it shr 8 and 0xFF) * 6 + (it and 0xFF)) / 10 }
            return px
        }
        val c = luma(crop)
        val f = luma(full)
        // Pixel (along, across) of a picture [width] wide, whichever way the window moves.
        fun at(along: Int, other: Int, width: Int) = if (across) other * width + along else along * width + other
        var best = 0
        var bestCost = Long.MAX_VALUE
        for (shift in 0..(length - window)) {
            var cost = 0L
            // Every 3rd pixel both ways: plenty to tell the places apart, and quick.
            for (a in 0 until window step 3) for (b in 0 until side step 3) {
                cost += kotlin.math.abs(c[at(a, b, crop.width)] - f[at(a + shift, b, full.width)])
            }
            if (cost < bestCost) { bestCost = cost; best = shift }
        }
        (best + window / 2f) / length
    }.getOrNull()
}

private suspend fun decode(url: String): Bitmap? = runCatching {
    val bytes = uz.akbarjon.oriel.data.Http.bytes(url)
    android.graphics.BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
}.getOrNull()

/** The position editor asked for (Home), shown over the whole app by [PositionEditorHost]. */
internal class EditorRequest(val photo: Photo, val origin: EditorOrigin?)

internal object PositionEditors {
    var open by mutableStateOf<EditorRequest?>(null)
}

/** The position editor over everything (the tabs and the navigation bar), while one is open. */
@Composable
fun PositionEditorHost() {
    val request = PositionEditors.open ?: return
    androidx.compose.runtime.key(request) {
        PositionEditor(request.photo, origin = request.origin) { if (PositionEditors.open === request) PositionEditors.open = null }
    }
}
