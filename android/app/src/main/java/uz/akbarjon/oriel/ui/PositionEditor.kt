package uz.akbarjon.oriel.ui

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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlin.math.abs
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.ChangeResult
import uz.akbarjon.oriel.core.Positions
import uz.akbarjon.oriel.data.Photo
import uz.akbarjon.oriel.data.Position

private val White = Color.White
private val Ink = Color(0xFF111114)

/** Which crop the editor is on: Unsplash's smart crop, the middle, or the window the user placed. */
private enum class Mode { Smart, Center, Custom }

/**
 * Where the wallpaper sits on this phone. The whole photo is shown with a phone-shaped window over it; drag (or tap)
 * to move the window across a wide photo (or up and down a tall one). Outside it is dimmed, it snaps to the middle
 * with a tick, and the thirds show while dragging. Smart and Center are one tap away. Set crops it there (Unsplash's
 * CDN does the cropping, so it stays full quality) and remembers it for this photo. Only this phone's.
 */
@Composable
fun PositionEditor(photo: Photo, onSet: (suspend (Position) -> ChangeResult)? = null, onDismiss: () -> Unit) {
    val snackbar = LocalSnackbar.current
    val haptics = LocalHapticFeedback.current
    val busy by Oriel.wallpapers.busy.collectAsState()
    val phase by Oriel.wallpapers.phase.collectAsState()
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    val screenAspect = screenW.toFloat() / screenH
    val bitmap by produceState<ImageBitmap?>(null, photo.id) { value = loadBitmap(photo.sized(1400, quality = 85, format = "webp")) }

    val start = remember(photo.id) { Positions.custom(photo.id) }
    var mode by remember(photo.id) {
        mutableStateOf(if (start != null) Mode.Custom else if (Positions.default() == Position.Center) Mode.Center else Mode.Smart)
    }
    // The window's center, 0..1 across (or down) the photo.
    var focus by remember(photo.id) { mutableFloatStateOf(start?.let { it.x } ?: 0.5f) }
    var focusY by remember(photo.id) { mutableFloatStateOf(start?.let { it.y } ?: 0.5f) }
    var dragging by remember { mutableStateOf(false) }

    Dialog(onDismissRequest = { if (!busy) onDismiss() }, properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)) {
        Box(Modifier.fillMaxSize().background(Color(0xFF0B0B0D))) {
            Column(Modifier.fillMaxSize().systemBarsPadding().padding(20.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Row(Modifier.fillMaxWidth().height(48.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("Position", style = MaterialTheme.typography.titleLarge, color = White, modifier = Modifier.weight(1f))
                    Box(Modifier.size(40.dp).clip(CircleShape).background(White.copy(alpha = 0.12f)).springClick(!busy, onClick = onDismiss),
                        contentAlignment = Alignment.Center) { Icon(OrielIcons.Close, "Close", tint = White, modifier = Modifier.size(18.dp)) }
                }
                Spacer(Modifier.height(16.dp))

                // The photo, whole, with the window over it.
                BoxWithConstraints(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                    val bmp = bitmap
                    if (bmp == null) {
                        CircularProgressIndicator(Modifier.size(28.dp), color = White, strokeWidth = 2.5.dp)
                        return@BoxWithConstraints
                    }
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
                        else -> 0.5f // Smart crops where Unsplash finds faces or detail: shown from the middle
                    }
                    val center by animateFloatAsState(shown, if (dragging) spring(stiffness = 3000f) else spring(dampingRatio = 0.8f, stiffness = 500f), label = "window")
                    val density = LocalDensity.current
                    val length = if (across) pw else ph
                    val window = if (across) ww else wh
                    val offset: Dp = (length * center - window / 2).coerceIn(0.dp, slack.coerceAtLeast(0.dp))

                    /** Puts the window's center at [px] (clamped to the photo), snapping to the middle with a tick. */
                    fun place(px: Float) {
                        val len = with(density) { length.toPx() }
                        val half = with(density) { window.toPx() } / 2 / len
                        val raw = (px / len).coerceIn(half, (1 - half).coerceAtLeast(half))
                        val snapped = if (abs(raw - 0.5f) < 0.025f) 0.5f else raw
                        val before = if (across) focus else focusY
                        if (snapped == 0.5f && (before != 0.5f || mode != Mode.Custom)) haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
                        if (across) focus = snapped else focusY = snapped
                        mode = Mode.Custom
                    }

                    Box(Modifier.size(pw, ph)
                        .pointerInput(across, pw, ph) {
                            detectDragGestures(
                                onDragStart = { dragging = true; place(if (across) it.x else it.y) },
                                onDragEnd = { dragging = false },
                                onDragCancel = { dragging = false },
                            ) { change, _ -> place(if (across) change.position.x else change.position.y) }
                        }
                        .pointerInput(across, pw, ph) { detectTapGestures { p -> place(if (across) p.x else p.y) } },
                    ) {
                        Image(bmp, photo.description, Modifier.fillMaxSize(), contentScale = ContentScale.FillBounds)
                        val dim = Color.Black.copy(alpha = 0.6f)
                        val ox = if (across) offset else 0.dp
                        val oy = if (across) 0.dp else offset
                        // The dimmed parts outside the window.
                        if (across) {
                            Box(Modifier.size(ox, ph).background(dim))
                            Box(Modifier.offset(x = ox + ww).size((pw - ox - ww).coerceAtLeast(0.dp), ph).background(dim))
                        } else {
                            Box(Modifier.size(pw, oy).background(dim))
                            Box(Modifier.offset(y = oy + wh).size(pw, (ph - oy - wh).coerceAtLeast(0.dp)).background(dim))
                        }
                        // The window: the phone's shape, with the thirds while dragging.
                        Box(Modifier.offset(x = ox, y = oy).size(ww, wh).border(2.dp, White, RoundedCornerShape(ww * 0.09f))) {
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

                Spacer(Modifier.height(20.dp))
                // Smart, Center, or where you put it.
                Row(Modifier.clip(CircleShape).background(White.copy(alpha = 0.1f)).padding(4.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    listOf(Mode.Smart to "Smart", Mode.Center to "Center", Mode.Custom to "Custom").forEach { (m, label) ->
                        val on = mode == m
                        val bg by animateColorAsState(if (on) White else Color.Transparent, label = "mode")
                        val fg by animateColorAsState(if (on) Ink else White.copy(alpha = 0.75f), label = "modeText")
                        Text(label, style = MaterialTheme.typography.labelLarge, color = fg,
                            modifier = Modifier.clip(CircleShape).background(bg).springClick {
                                haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
                                mode = m
                                if (m == Mode.Center) { focus = 0.5f; focusY = 0.5f }
                            }.padding(horizontal = 18.dp, vertical = 10.dp))
                    }
                }
                Spacer(Modifier.height(16.dp))
                PrimaryButton(
                    phase?.text ?: "Set",
                    onClick = {
                        val position = when (mode) {
                            Mode.Smart -> Position.Smart
                            Mode.Center -> Position.Center
                            Mode.Custom -> Position.Focus(focus, focusY)
                        }
                        Oriel.scope.launch(Dispatchers.Main) {
                            // A picture not set yet (the AI tab) is set with this crop; the wallpaper on screen is cropped again.
                            when (val result = onSet?.invoke(position) ?: Oriel.wallpapers.reposition(photo, position)) {
                                is ChangeResult.Applied -> { onDismiss(); snackbar.showSnackbar("Position set") }
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
    }
}
