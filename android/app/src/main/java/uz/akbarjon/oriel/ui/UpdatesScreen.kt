package uz.akbarjon.oriel.ui

import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.animation.core.LinearEasing
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin
import androidx.compose.runtime.remember
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.layout.boundsInParent
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.geometry.lerp
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import uz.akbarjon.oriel.BuildConfig
import uz.akbarjon.oriel.core.UpdateState
import uz.akbarjon.oriel.core.Updater

/**
 * Updates, opened from Settings > About: this version in the middle (green bubbles glowing around it while it's the
 * latest), and at the bottom one button that does the next step: check, then install, or confirm when Android asks.
 * Opening it checks right away.
 */
@Composable
fun UpdatesScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val c = Palette.colors
    val update by Updater.state.collectAsState()

    LaunchedEffect(Unit) { Updater.checkSoon(context, force = true, manual = true) }

    androidx.activity.compose.BackHandler(onBack = onBack)
    // The glows lie under the whole page (the title too, nothing hides them), centered on the version in the middle.
    var focus by remember { mutableStateOf<Rect?>(null) }
    var page by remember { mutableStateOf<androidx.compose.ui.layout.LayoutCoordinates?>(null) }
    Box(Modifier.fillMaxSize().onGloballyPositioned { page = it }) {
        Glows(
            when (update) {
                is UpdateState.Checking, is UpdateState.Downloading, is UpdateState.Installing -> GlowMood.Working
                is UpdateState.ReadyToRestart -> GlowMood.Ready
                else -> GlowMood.Calm
            },
            focus, Modifier.matchParentSize(),
        )
        Column(Modifier.fillMaxSize().readableWidth()) {
            Row(Modifier.fillMaxWidth().height(72.dp).padding(start = 8.dp, end = 24.dp, top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                androidx.compose.material3.IconButton(onBack) {
                    Icon(OrielIcons.ChevronRight, "Back", Modifier.size(24.dp).graphicsLayer { rotationZ = 180f })
                }
                Text("Updates", style = MaterialTheme.typography.headlineMedium)
            }

            Box(Modifier.weight(1f).fillMaxWidth().onGloballyPositioned { area -> page?.takeIf { it.isAttached }?.let { focus = it.localBoundingBoxOf(area) } }, contentAlignment = Alignment.Center) {
                Column(Modifier.fillMaxWidth().padding(horizontal = 32.dp).animateContentSize(), horizontalAlignment = Alignment.CenterHorizontally) {
                    Logo(64.dp)
                    Spacer(Modifier.height(16.dp))
                    Text("Oriel ${BuildConfig.VERSION_NAME}", style = MaterialTheme.typography.titleLarge)
                    // Up to date says it with the bubbles; every other step in words.
                    val status = when (val u = update) {
                        is UpdateState.Checking -> "Checking for updates…"
                        is UpdateState.Downloading -> u.progress?.let { "Downloading ${u.version} · ${(it * 100).toInt()}%" } ?: "Downloading ${u.version}…"
                        is UpdateState.Installing -> "Installing ${u.version}. Oriel opens again in a moment."
                        is UpdateState.ReadyToRestart -> "Version ${u.version} is ready"
                        is UpdateState.NeedsConfirm -> "Version ${u.version} is ready to install"
                        is UpdateState.Available -> "Version ${u.version} is out (this test build doesn't install it)"
                        is UpdateState.Failed -> u.message
                        UpdateState.UpToDate, UpdateState.Idle -> null
                    }
                    if (status != null) {
                        Spacer(Modifier.height(4.dp))
                        SlidingText(status, MaterialTheme.typography.bodyMedium, c.muted)
                    }
                    (update as? UpdateState.Downloading)?.let { d ->
                        Spacer(Modifier.height(16.dp))
                        val shown by animateFloatAsState(d.progress ?: 0f, label = "progress")
                        if (d.progress == null) LinearProgressIndicator(Modifier.fillMaxWidth().height(6.dp).clip(CircleShape), color = c.ink, trackColor = c.line)
                        else LinearProgressIndicator({ shown }, Modifier.fillMaxWidth().height(6.dp).clip(CircleShape), color = c.ink, trackColor = c.line,
                            gapSize = 0.dp, drawStopIndicator = {})
                    }
                }
            }

            val u = update
            val working = u is UpdateState.Checking || u is UpdateState.Downloading || u is UpdateState.Installing
            PrimaryButton(
                when (u) {
                    is UpdateState.NeedsConfirm -> "Install ${u.version}"
                    is UpdateState.ReadyToRestart -> "Restart to update"
                    is UpdateState.Checking -> "Checking…"
                    is UpdateState.Downloading -> "Downloading…"
                    is UpdateState.Installing -> "Installing…"
                    is UpdateState.Failed -> "Try again"
                    else -> "Check for updates"
                },
                // Restart into a downloaded update; Android's confirmation if one waits; else check (which downloads a newer one).
                {
                    when {
                        u is UpdateState.ReadyToRestart -> Updater.restartToUpdate(context)
                        !Updater.confirm(context) -> Updater.checkSoon(context, force = true, manual = true)
                    }
                },
                Modifier.fillMaxWidth().padding(horizontal = 24.dp), busy = working,
                icon = { Icon(if (u is UpdateState.NeedsConfirm) OrielIcons.Download else OrielIcons.Refresh, null, Modifier.size(18.dp)) },
            )
            // Test builds can't install a release: this plays one through, for trying the page.
            if (!Updater.enabled) {
                TextAction("Demo an update (test build)", { Updater.demo(context) }, Modifier.align(Alignment.CenterHorizontally).padding(top = 8.dp),
                    color = c.muted, enabled = !working)
            }
            Spacer(Modifier.height(16.dp))
            BottomBarSpacer()
        }
    }
}

/** What the glows say: calm (green, drifting), working (yellow, gathered, pulsing), ready (blue, circling the logo). */
private enum class GlowMood { Calm, Working, Ready }

/**
 * Four big, soft glows behind the version, always there (they fade in once, when the page opens).
 * - Calm: green, each drifting on its own slow path (17 to 31 seconds a sweep, eased at the turns) and breathing a
 *   little, so they never line up the same way twice.
 * - Working (checking, downloading, installing): they warm to yellow, gather in the middle and pulse together, big,
 *   small, big.
 * - Ready to restart: they cool to blue and violet and circle the logo slowly, like they're waiting for the tap.
 * Every change glides over a second or so.
 */
@Composable
private fun Glows(mood: GlowMood, focus: Rect?, modifier: Modifier = Modifier) {
    val appear = remember { Animatable(0f) }
    LaunchedEffect(Unit) { appear.animateTo(1f, tween(1200, easing = FastOutSlowInEasing)) }
    val alpha = appear.value
    val glide = tween<Float>(1400, easing = FastOutSlowInEasing)
    val working by animateFloatAsState(if (mood == GlowMood.Working) 1f else 0f, glide, label = "working")
    val ready by animateFloatAsState(if (mood == GlowMood.Ready) 1f else 0f, glide, label = "ready")
    val drift = rememberInfiniteTransition(label = "drift")
    val ease = CubicBezierEasing(0.45f, 0f, 0.55f, 1f)   // like a sine: slow at both ends of each sweep
    @Composable
    fun wave(ms: Int, label: String) = drift.animateFloat(0f, 1f, infiniteRepeatable(tween(ms, easing = ease), RepeatMode.Reverse), label = label)
    // Each glow: two sweeps (x and y, different lengths, so its path is a slow loop) and a breath.
    val waves = listOf(
        Triple(wave(23_000, "x1"), wave(17_000, "y1"), wave(11_000, "b1")),
        Triple(wave(29_000, "x2"), wave(21_000, "y2"), wave(13_000, "b2")),
        Triple(wave(19_000, "x3"), wave(31_000, "y3"), wave(15_000, "b3")),
        Triple(wave(27_000, "x4"), wave(24_000, "y4"), wave(12_000, "b4")),
    )
    // Working: one shared heartbeat, big and small. Ready: one slow lap around the logo every 14 seconds.
    val pulse by wave(1500, "pulse")
    val orbit by drift.animateFloat(0f, (2 * PI).toFloat(), infiniteRepeatable(tween(14_000, easing = LinearEasing)), label = "orbit")
    Canvas(modifier.graphicsLayer { this.alpha = alpha }) {
        // Their paths are laid out over the focus (the middle of the page); they spill freely past it.
        val area = focus ?: Rect(Offset.Zero, size)
        val w = area.width
        val h = area.height
        val unit = minOf(w, h)
        val mid = area.center
        GLOWS.forEachIndexed { i, g ->
            val (x, y, b) = waves[i]
            val home = Offset(area.left + w * (g.x + g.dx * (x.value - 0.5f)), area.top + h * (g.y + g.dy * (y.value - 0.5f)))
            // Gathered, each still sits a little off the middle, so the four read as one living glow, not a disc.
            val near = Offset(mid.x + (home.x - mid.x) * 0.18f, mid.y + (home.y - mid.y) * 0.18f)
            val angle = orbit + i * (PI / 2).toFloat()
            val ring = Offset(mid.x + cos(angle) * unit * 0.24f, mid.y + sin(angle) * unit * 0.24f)
            val center = lerp(lerp(home, near, working), ring, ready)
            val breath = 0.92f + 0.16f * b.value
            val beat = 0.62f + 0.38f * pulse
            val size = breath + (beat - breath) * working + (0.62f * breath - breath) * ready
            val radius = unit * g.radius * size
            val color = androidx.compose.ui.graphics.lerp(androidx.compose.ui.graphics.lerp(g.color, g.warm, working), g.cool, ready)
            val strength = g.strength * (1f + 0.25f * working + 0.2f * ready)
            drawCircle(
                Brush.radialGradient(
                    0f to color.copy(alpha = strength), 0.45f to color.copy(alpha = strength * 0.45f), 1f to Color.Transparent,
                    center = center, radius = radius,
                ),
                radius, center,
            )
        }
    }
}

/** Where a glow sits (0..1 of the area), how far it wanders, its size (of the shorter side), its green, yellow and blue. */
private class Glow(
    val x: Float, val y: Float, val dx: Float, val dy: Float, val radius: Float,
    val color: Color, val strength: Float, val warm: Color, val cool: Color,
)

private val GLOWS = listOf(
    Glow(0.30f, 0.38f, 0.30f, 0.22f, 0.62f, Color(0xFF34D399), 0.42f, Color(0xFFFACC15), Color(0xFF60A5FA)),   // emerald / amber yellow / sky
    Glow(0.72f, 0.32f, 0.26f, 0.26f, 0.55f, Color(0xFF6EE7B7), 0.38f, Color(0xFFFDE047), Color(0xFF818CF8)),   // mint / lemon / indigo
    Glow(0.62f, 0.68f, 0.32f, 0.20f, 0.66f, Color(0xFF10B981), 0.32f, Color(0xFFF59E0B), Color(0xFF38BDF8)),   // deep green / amber / cyan
    Glow(0.32f, 0.70f, 0.24f, 0.28f, 0.50f, Color(0xFF2DD4BF), 0.30f, Color(0xFFFBBF24), Color(0xFFA78BFA)),   // teal / honey / violet
)
