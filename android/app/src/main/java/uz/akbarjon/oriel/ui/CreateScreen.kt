package uz.akbarjon.oriel.ui

import uz.akbarjon.oriel.ui.Motion
import uz.akbarjon.oriel.ui.Motion.fadeThrough
import uz.akbarjon.oriel.ui.Motion.slideThrough
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.border
import dev.chrisbanes.haze.HazeState
import dev.chrisbanes.haze.HazeStyle
import dev.chrisbanes.haze.HazeTint
import dev.chrisbanes.haze.hazeEffect
import dev.chrisbanes.haze.hazeSource
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.ime
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import androidx.compose.material3.SnackbarHostState
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.ChangeResult
import uz.akbarjon.oriel.core.Positions
import uz.akbarjon.oriel.data.OrielException
import uz.akbarjon.oriel.data.Photo
import uz.akbarjon.oriel.data.Pollinations

/** The AI colors: indigo, violet, orchid, soft blue (the same as the desktop apps' Create page). */
private val Indigo = Color(0xFF5B5BF7)
private val Violet = Color(0xFF8B5CF6)
private val Orchid = Color(0xFFC084FC)
private val SoftBlue = Color(0xFF60A5FA)
private val AiColors = listOf(Indigo, Violet, Orchid, SoftBlue, Indigo)

/** Glowing bubbles behind the page: where each sits (0..1 of the page), its size, color, and how far and slow it drifts. */
private class Bubble(val x: Float, val y: Float, val radius: Dp, val color: Color, val alpha: Float, val dx: Dp, val dy: Dp, val ms: Int)

private val BUBBLES = listOf(
    Bubble(0.12f, 0.18f, 200.dp, Indigo, 0.55f, 110.dp, 90.dp, 9_000),
    Bubble(0.88f, 0.32f, 180.dp, Violet, 0.50f, -120.dp, 110.dp, 11_000),
    Bubble(0.22f, 0.66f, 170.dp, SoftBlue, 0.42f, 120.dp, -90.dp, 8_000),
    Bubble(0.82f, 0.84f, 200.dp, Orchid, 0.45f, -110.dp, -120.dp, 12_000),
)

/** A look for the picture: words added after the user's own. */
private class Look(val label: String, val words: String?)

private val LOOKS = listOf(
    Look("Any", null),
    Look("Photo", "photorealistic, natural light, sharp detail"),
    Look("Painting", "oil painting, visible brush strokes, rich colors"),
    Look("Watercolor", "soft watercolor painting, flowing pigments"),
    Look("Anime", "anime background art, soft light, detailed scenery"),
    Look("Minimal", "minimalist, flat colors, simple shapes, lots of empty space"),
    Look("Abstract", "abstract art, flowing shapes, smooth gradients"),
    Look("3D", "3D render, soft studio lighting, clay look"),
    Look("Pixel art", "pixel art, retro game scene"),
)

private val IDEAS = listOf(
    "a quiet lake at dawn", "a cozy cabin in the snow", "a lighthouse in a storm", "lavender under a pink sky",
    "a whale in the clouds", "a neon city after rain", "the northern lights", "a fox in a mossy forest",
    "an astronaut fishing on the moon", "cherry blossoms by a river", "a desert with two moons", "a koi pond from above",
    "a castle above the clouds", "a cat on a rooftop at night", "an underwater city", "waves of colorful dunes",
)

/** Kept for the life of the app, so leaving the tab doesn't lose the picture. */
private object CreateModel {
    var text by mutableStateOf("")
    var look by mutableStateOf(LOOKS[0])
    var ideas by mutableStateOf(IDEAS.shuffled().take(6))
    var step by mutableStateOf<String?>(null)
    var picture by mutableStateOf<Photo?>(null)
    var lastWords = ""
    var lastLook: Look = LOOKS[0]
    var applied by mutableStateOf(false)

    /** "I'm feeling lucky": an idea and a look at random, made right away. */
    fun lucky(scope: CoroutineScope, snackbar: SnackbarHostState) {
        if (step != null) return
        text = IDEAS.random()
        look = LOOKS.random()
        create(scope, snackbar)
    }

    fun create(scope: CoroutineScope, snackbar: SnackbarHostState, words: String = text, look: Look = this.look) {
        val prompt = words.trim()
        if (prompt.isEmpty() || step != null) return
        step = "Imagining…"
        applied = false
        // The last picture goes now: it never shows again before the new one.
        picture = null
        scope.launch {
            try {
                val made = withContext(Dispatchers.IO) {
                    Pollinations.create(prompt, look.words) { s -> scope.launch(Dispatchers.Main) { step = s } }
                }
                // Fetched while "Painting…" still shows, so the picture is ready when its reveal starts.
                withContext(Dispatchers.IO) { runCatching { loadBitmap(made.rawUrl) } }
                lastWords = prompt
                lastLook = look
                picture = made
            } catch (e: OrielException) {
                snackbar.showSnackbar(e.message ?: "Couldn't create it. Try again.")
            } catch (e: Exception) {
                snackbar.showSnackbar("Couldn't create it. Try again.")
            } finally {
                step = null
            }
        }
    }
}

/** The gradient turning slowly around the shape's edge, [width] thick, with its colors' glow around it. */
private fun Modifier.aiRing(shape: Shape, width: Dp = 2.dp): Modifier = composed {
    val angle by rememberInfiniteTransition(label = "ring").animateFloat(
        0f, 360f, infiniteRepeatable(tween(6000, easing = LinearEasing)), label = "angle",
    )
    shadow(18.dp, shape, ambientColor = Violet, spotColor = Indigo)
        .clip(shape)
        .drawBehind { rotate(angle) { drawCircle(Brush.sweepGradient(AiColors), radius = size.maxDimension) } }
        .padding(width)
}

/** AI Studio: a few words in, an AI wallpaper out, shown first, placed and set when asked. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun CreateScreen() {
    val m = CreateModel
    val c = Palette.colors
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    val snackbar = LocalSnackbar.current
    val focus = LocalFocusManager.current
    val phase by Oriel.wallpapers.phase.collectAsState()
    val busy by Oriel.wallpapers.busy.collectAsState()

    // Just above the keyboard or just above the tab bar, whichever is higher. The keyboard's height moves frame by
    // frame as it slides, so the words follow it smoothly both ways (no drop to the tab bar's place, then a jump back).
    val keyboard = WindowInsets.ime.asPaddingValues().calculateBottomPadding()
    val bottom = maxOf(keyboard + 10.dp, bottomBarSpace())
    val words = remember { FocusRequester() }
    var cropping by remember { mutableStateOf(false) }
    // Where the picture sits on this screen, then set with it.
    m.picture?.takeIf { cropping }?.let { photo ->
        PositionEditor(photo, onSet = { position ->
            Positions.set(photo.id, position)
            Oriel.wallpapers.apply(photo, "created", publish = true, track = false).also { if (it is ChangeResult.Applied) m.applied = true }
        }) { cropping = false }
    }

    val glass = remember { HazeState() }
    Box(Modifier.fillMaxSize()) {
    // Glowing bubbles behind everything; the canvas is frosted glass over them.
    Box(Modifier.fillMaxSize().hazeSource(glass)) { Bubbles() }
    Column(Modifier.fillMaxSize().padding(bottom = bottom)) {
        ScreenTitle("AI Studio") {
            Row(
                Modifier.pressable(CircleShape, c.surface, enabled = m.step == null, border = c.line) {
                    focus.clearFocus(); m.lucky(scope, snackbar)
                }
                    .padding(horizontal = 14.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(OrielIcons.Dice, null, tint = c.ink, modifier = Modifier.size(18.dp))
                Spacer(Modifier.size(8.dp))
                Text("I'm feeling lucky", style = MaterialTheme.typography.labelLarge, color = c.ink)
            }
        }

        // The canvas: ideas, the picture being made, or the picture.
        Box(
            Modifier.weight(1f).fillMaxWidth().padding(horizontal = 16.dp).clip(RoundedCornerShape(28.dp))
                .hazeEffect(glass, HazeStyle(backgroundColor = c.paper, tint = HazeTint(c.surface.copy(alpha = 0.55f)), blurRadius = 36.dp, noiseFactor = 0f))
                .border(1.dp, c.line.copy(alpha = 0.6f), RoundedCornerShape(28.dp)),
            contentAlignment = Alignment.Center,
        ) {
            val state = when {
                m.step != null -> "making"
                m.picture != null -> "picture"
                else -> "ideas"
            }
            AnimatedContent(state, transitionSpec = { fadeThrough() }, label = "canvas") { s ->
                when (s) {
                    "making" -> Making(m.step ?: "")
                    "picture" -> m.picture?.let { photo ->
                        Picture(photo, setting = busy, onCrop = { cropping = true }, label = when {
                            busy -> phase?.text ?: "Changing…"
                            m.applied -> "Set ✓"
                            else -> "Set"
                        }) {
                            scope.launch {
                                when (val r = Oriel.wallpapers.apply(photo, "created", publish = true, track = false)) {
                                    is ChangeResult.Applied -> m.applied = true
                                    is ChangeResult.Failed -> snackbar.showSnackbar(r.message)
                                    ChangeResult.Skipped -> {}
                                }
                            }
                        }
                    }
                    // Ideas, then Shuffle. They scroll when the canvas is short (the keyboard is up) and sit centered when it isn't.
                    else -> BoxWithConstraints(Modifier.fillMaxSize()) {
                        Column(
                            Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).heightIn(min = maxHeight).padding(24.dp),
                            horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center,
                        ) {
                            FlowRow(
                                horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterHorizontally),
                                verticalArrangement = Arrangement.spacedBy(8.dp),
                            ) {
                                m.ideas.forEach { idea ->
                                    ToggleChip(idea.replaceFirstChar { it.uppercase() }, false, {
                                        // Fills in the words, ready to change or send.
                                        m.text = idea
                                        words.requestFocus()
                                    })
                                }
                            }
                            Spacer(Modifier.height(10.dp))
                            Row(
                                Modifier.pressable(CircleShape, Color.Transparent) { m.ideas = IDEAS.shuffled().take(6) }
                                    .padding(horizontal = 14.dp, vertical = 10.dp),
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Icon(OrielIcons.Refresh, null, tint = c.ink, modifier = Modifier.size(16.dp))
                                Spacer(Modifier.size(8.dp))
                                Text("Shuffle", style = MaterialTheme.typography.labelLarge, color = c.ink)
                            }
                        }
                    }
                }
            }
        }

        // Looks: one is on.
        Row(
            Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(top = 12.dp, bottom = 10.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) { LOOKS.forEach { look -> ToggleChip(look.label, look == m.look, { m.look = look }) } }

        // The words, in a turning gradient ring.
        Box(Modifier.padding(horizontal = 16.dp).aiRing(CircleShape)) {
            OrielTextField(
                m.text, { m.text = it; m.applied = false }, Modifier.focusRequester(words), placeholder = "Describe a wallpaper",
                leadingIcon = OrielIcons.Sparkles, background = c.paper,
                trailing = {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(
                        Modifier.padding(end = 6.dp).size(44.dp)
                            .pressable(CircleShape, c.ink, enabled = m.step == null && m.text.isNotBlank(), pressed = 0.9f) {
                                focus.clearFocus(); m.create(scope, snackbar)
                            },
                        contentAlignment = Alignment.Center,
                    ) {
                        if (m.step != null) CircularProgressIndicator(Modifier.size(18.dp), color = c.paper, strokeWidth = 2.dp)
                        else Icon(OrielIcons.ArrowUp, "Create", tint = c.paper, modifier = Modifier.size(20.dp))
                    }
                    }
                },
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Go),
                keyboardActions = KeyboardActions(onGo = { focus.clearFocus(); m.create(scope, snackbar) }),
            )
        }

    }
    }
}

/** Four soft glowing bubbles drifting and swelling behind the page, each on its own path and pace. */
@Composable
private fun Bubbles() {
    val drift = rememberInfiniteTransition(label = "bubbles")
    val moves = BUBBLES.map { b ->
        drift.animateFloat(0f, 1f, infiniteRepeatable(tween(b.ms, easing = FastOutSlowInEasing), RepeatMode.Reverse), label = "bubble")
    }
    val swells = BUBBLES.map { b ->
        drift.animateFloat(1f, 1.2f, infiniteRepeatable(tween(b.ms * 6 / 10, easing = FastOutSlowInEasing), RepeatMode.Reverse), label = "swell")
    }
    Canvas(Modifier.fillMaxSize()) {
        BUBBLES.forEachIndexed { i, b ->
            val t = moves[i].value
            val center = Offset(size.width * b.x + b.dx.toPx() * t, size.height * b.y + b.dy.toPx() * t)
            val r = b.radius.toPx() * swells[i].value
            drawCircle(
                Brush.radialGradient(listOf(b.color.copy(alpha = b.alpha), b.color.copy(alpha = b.alpha * 0.35f), Color.Transparent), center, r),
                radius = r, center = center,
            )
        }
    }
}

/** While the picture is made: the step ("Imagining…", "Painting…"), with the bubbles glowing behind. */
@Composable
private fun Making(step: String) {
    val pulse by rememberInfiniteTransition(label = "making").animateFloat(
        0.85f, 1.15f, infiniteRepeatable(tween(900), RepeatMode.Reverse), label = "pulse",
    )
    Column(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
        Icon(OrielIcons.Sparkles, null, tint = Violet, modifier = Modifier.size(34.dp).graphicsLayer { scaleX = pulse; scaleY = pulse })
        Spacer(Modifier.height(12.dp))
        SlidingText(step, style = MaterialTheme.typography.titleMedium)
    }
}

/** The picture, with its two actions on it. */
@Composable
private fun Picture(photo: Photo, setting: Boolean, label: String, onCrop: () -> Unit, onSet: () -> Unit) {
    // A new picture eases in like the desktop apps' photos: a little bigger and clear, then settling to full size.
    val reveal = remember(photo.id) { Animatable(0f) }
    LaunchedEffect(photo.id) { reveal.animateTo(1f, tween(700, easing = FastOutSlowInEasing)) }
    Box(Modifier.fillMaxSize()) {
        RemoteImage(photo.rawUrl, photo.description, Modifier.fillMaxSize().graphicsLayer {
            alpha = (reveal.value / 0.65f).coerceAtMost(1f)
            val scale = 1.1f - 0.1f * reveal.value
            scaleX = scale; scaleY = scale
        })
        Row(
            Modifier.align(Alignment.BottomCenter).fillMaxWidth()
                .background(Brush.verticalGradient(listOf(Color.Transparent, Color(0x99000000))))
                .padding(start = 18.dp, end = 12.dp, top = 40.dp, bottom = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                photo.description?.replaceFirstChar { it.uppercase() } ?: "", color = Color.White,
                style = MaterialTheme.typography.bodyLarge, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f),
            )
            RoundIconButton(OrielIcons.Crop, "Position", onCrop, size = 44.dp, enabled = !setting)

            Spacer(Modifier.size(8.dp))
            PrimaryButton(label, onSet, Modifier.height(44.dp), busy = setting, container = Color.White, content = Color(0xFF141414))
        }
    }
}
