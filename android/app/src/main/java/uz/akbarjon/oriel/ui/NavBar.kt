package uz.akbarjon.oriel.ui

import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.windowInsetsBottomHeight
import androidx.compose.foundation.layout.displayCutoutPadding
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.offset
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.unit.Dp
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.sp
import androidx.compose.animation.Crossfade
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch
import dev.chrisbanes.haze.HazeState
import dev.chrisbanes.haze.HazeStyle
import dev.chrisbanes.haze.HazeTint
import dev.chrisbanes.haze.hazeEffect

/** How a tab's icon plays when the tab is picked. */
internal enum class Play { Hop, Compass, Flip, Spin }

enum class Tab(val label: String, val icon: ImageVector, val selectedIcon: ImageVector, internal val animation: Play) {
    Home("Home", OrielIcons.Home, OrielIcons.HomeFilled, Play.Hop),
    Explore("Explore", OrielIcons.Compass, OrielIcons.CompassFilled, Play.Compass),
    History("History", OrielIcons.History, OrielIcons.History, Play.Flip),
    Settings("Settings", OrielIcons.Settings, OrielIcons.Settings, Play.Spin),
}

/**
 * The band at the bottom the floating bar sits in (around it, and under it down to the screen's edge): it takes taps
 * and does nothing with them, so a finger that misses the bar doesn't press a row or a switch scrolled under it.
 * Invisible; drawn under the bar.
 */
@Composable
fun NavBarGuard(modifier: Modifier = Modifier, vertical: Boolean = false) {
    if (vertical) {
        // On its side: the strip along the right edge the bar stands in, top to bottom.
        Box(
            modifier.fillMaxHeight().systemBarsPadding().displayCutoutPadding().width(NavBarHeight + NavBarMargin * 2)
                .pointerInput(Unit) { awaitEachGesture { awaitFirstDown(requireUnconsumed = false).consume() } },
        )
        return
    }
    Box(
        modifier.fillMaxWidth().navigationBarsPadding().height(NavBarHeight + NavBarMargin * 2)
            .pointerInput(Unit) { awaitEachGesture { awaitFirstDown(requireUnconsumed = false).consume() } },
    )
    // The system's own strip under it (the gesture handle's) too.
    Box(
        modifier.fillMaxWidth().windowInsetsBottomHeight(WindowInsets.navigationBars)
            .pointerInput(Unit) { awaitEachGesture { awaitFirstDown(requireUnconsumed = false).consume() } },
    )
}

/**
 * The navigation bar: a floating capsule over the content. One soft pill slides to the picked tab, stretching like
 * a drop of water (its leading edge moves first, the trailing edge catches up), and the picked tab's icon plays a little
 * animation: the house hops, the compass swings round to north, the clock winds back, the friends pulse, the gear spins. Over Home's photo
 * it turns dark and see-through ([onPhoto]). With the phone on its side ([vertical]) it stands on the right edge.
 */
@Composable
fun NavBar(
    selected: Tab, onSelect: (Tab) -> Unit, haze: HazeState, modifier: Modifier = Modifier, onPhoto: Boolean = false,
    vertical: Boolean = false,
    /** While an update waits: one more item after Settings, Update, that opens Settings > Updates. */
    onUpdate: (() -> Unit)? = null,
    /** Settings > Updates is open: the pill stretches over Settings and Update, both being where you are. */
    onUpdates: Boolean = false,
) {
    val c = Palette.colors
    val haptics = LocalHapticFeedback.current
    val shape = CircleShape
    // Frosted: what scrolls behind the bar is blurred (Android 12+), under a light veil of the bar's color.
    val tint by animateColorAsState(if (onPhoto) Color(0xFF16161A).copy(alpha = 0.35f) else c.raised.copy(alpha = 0.62f), tween(300), label = "navBar")
    val edge by animateColorAsState(if (onPhoto) Color.White.copy(alpha = 0.12f) else c.line, tween(300), label = "navEdge")
    val ink = if (onPhoto) Color.White else c.ink
    val pillColor by animateColorAsState(ink.copy(alpha = if (onPhoto) 0.16f else 0.08f), tween(300), label = "pillBg")
    val frame = if (vertical) {
        modifier.systemBarsPadding().displayCutoutPadding().padding(NavBarMargin).width(NavBarHeight).heightIn(max = 380.dp).fillMaxHeight()
    } else {
        modifier.navigationBarsPadding().padding(horizontal = 24.dp, vertical = NavBarMargin).fillMaxWidth().height(NavBarHeight)
    }
    // Held (press and hold a tab): where the finger is along the bar (pixels), and how long the tabs' row is. While
    // held, the bar swells a little, and leans gently the other way from the pill (it slides right, the bar drifts left).
    var heldAt by remember { mutableStateOf<Float?>(null) }
    var rowPx by remember { mutableFloatStateOf(0f) }
    val swell by animateFloatAsState(if (heldAt != null) 1.04f else 1f, spring(dampingRatio = 0.5f, stiffness = 400f), label = "barSwell")
    val leanTarget = heldAt?.takeIf { rowPx > 0f }?.let { -((it / rowPx) - 0.5f) * 2f } ?: 0f
    val lean by animateFloatAsState(leanTarget, spring(dampingRatio = 0.8f, stiffness = 300f), label = "barLean")
    Box(frame.graphicsLayer {
        scaleX = swell
        scaleY = swell
        // At most this far either way: a lean, not a slide.
        val shift = lean * 6.dp.toPx()
        if (vertical) translationY = shift else translationX = shift
    }) {
    // Only the bar's background is clipped to the capsule; the icons draw on top, free to hop and flip past its edge.
    Box(
        Modifier.matchParentSize()
            .shadow(if (onPhoto) 0.dp else 18.dp, shape, clip = false, ambientColor = Color.Black.copy(alpha = 0.18f), spotColor = Color.Black.copy(alpha = 0.18f))
            .clip(shape)
            .hazeEffect(haze, HazeStyle(backgroundColor = if (onPhoto) Color.Black else c.paper, tint = HazeTint(tint), blurRadius = 24.dp, noiseFactor = 0f))
            .border(1.dp, edge, shape),
    )
    BoxWithConstraints(Modifier.fillMaxSize().padding(6.dp)) {
        val itemSize = (if (vertical) maxHeight else maxWidth) / (Tab.entries.size + if (onUpdate != null) 1 else 0)
        val index = selected.ordinal
        var from by remember { mutableIntStateOf(index) }
        val forward = index >= from
        val fast = spring<Dp>(dampingRatio = 0.75f, stiffness = 900f)
        val slow = spring<Dp>(dampingRatio = 0.7f, stiffness = 260f)
        // Held: the pill is picked up and follows the finger (where along the bar, in pixels), until it's let go.
        val density = LocalDensity.current
        val itemPx = with(density) { itemSize.toPx() }
        SideEffect { rowPx = itemPx * Tab.entries.size }
        val held = heldAt?.let { with(density) { (it.toDp() - itemSize / 2).coerceIn(0.dp, itemSize * (Tab.entries.size - 1)) } }
        val follow = spring<Dp>(dampingRatio = 0.85f, stiffness = 1400f)
        val start by animateDpAsState(held ?: (itemSize * index), if (held != null) follow else if (forward) slow else fast, label = "pillStart")
        val span = if (onUpdates && onUpdate != null && selected == Tab.Settings) 2 else 1
        val end by animateDpAsState(held?.plus(itemSize) ?: (itemSize * (index + span)), if (held != null) follow else if (forward) fast else slow, label = "pillEnd")
        // Picked up, it swells a little, like something lifted off the bar.
        val lifted by animateFloatAsState(if (held != null) 1.08f else 1f, spring(dampingRatio = 0.55f, stiffness = 500f), label = "pillLift")
        LaunchedEffect(index) { from = index }
        Box(
            (if (vertical) Modifier.offset(y = start).height(end - start).fillMaxWidth() else Modifier.offset(x = start).width(end - start).fillMaxHeight())
                .graphicsLayer { scaleX = lifted; scaleY = lifted }.clip(shape).background(pillColor),
        )

        val pick = { tab: Tab ->
            if (tab != selected) haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
            onSelect(tab)
        }
        val currentPick by rememberUpdatedState(pick)
        /**
         * Press and hold any tab: a buzz like a favorite's, the bar swells, the pill jumps there and follows the finger
         * (a tick at each tab it crosses), and letting go goes to the tab under it. A short press stays an ordinary tap.
         */
        val holdToSlide = Modifier.pointerInput(index, itemPx, vertical) {
            fun along(o: Offset) = if (vertical) o.y else o.x
            fun tabAt(px: Float) = (px / itemPx).toInt().coerceIn(0, Tab.entries.size - 1)
            awaitEachGesture {
                val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                // Any tab (not the Update item): held, the pill jumps under the finger and follows it.
                if (along(down.position) >= itemPx * Tab.entries.size) return@awaitEachGesture
                // Let go or moved before the hold time: not a hold (the tap, or the page's own swipe, goes on as usual).
                val early = withTimeoutOrNull(viewConfiguration.longPressTimeoutMillis) {
                    while (true) {
                        val change = awaitPointerEvent(PointerEventPass.Initial).changes.firstOrNull { it.id == down.id } ?: return@withTimeoutOrNull true
                        if (!change.pressed || (change.position - down.position).getDistance() > viewConfiguration.touchSlop) return@withTimeoutOrNull true
                    }
                    @Suppress("UNREACHABLE_CODE") true
                }
                if (early != null) return@awaitEachGesture
                haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                heldAt = along(down.position)
                var over = tabAt(along(down.position))
                try {
                    while (true) {
                        val change = awaitPointerEvent(PointerEventPass.Initial).changes.firstOrNull { it.id == down.id } ?: break
                        // Taken here, so the tab under the finger doesn't also see a tap.
                        change.consume()
                        if (!change.pressed) {
                            currentPick(Tab.entries[over])
                            break
                        }
                        heldAt = along(change.position)
                        val now = tabAt(along(change.position))
                        if (now != over) { over = now; haptics.performHapticFeedback(HapticFeedbackType.SegmentTick) }
                    }
                } finally {
                    heldAt = null
                }
            }
        }
        if (vertical) {
            Column(Modifier.fillMaxSize().then(holdToSlide), horizontalAlignment = Alignment.CenterHorizontally) {
                Tab.entries.forEach { tab -> NavItem(tab, tab == selected, onPhoto, Modifier.weight(1f).fillMaxWidth()) { pick(tab) } }
                if (onUpdate != null) UpdateNavItem(onPhoto, Modifier.weight(1f).fillMaxWidth(), onUpdate)
            }
        } else {
            Row(Modifier.fillMaxSize().then(holdToSlide), verticalAlignment = Alignment.CenterVertically) {
                Tab.entries.forEach { tab -> NavItem(tab, tab == selected, onPhoto, Modifier.weight(1f)) { pick(tab) } }
                if (onUpdate != null) UpdateNavItem(onPhoto, Modifier.weight(1f), onUpdate)
            }
        }
    }
    }
}

/** Update, after Settings: its arrow in a filled circle, so it reads as something waiting. */
@Composable
private fun UpdateNavItem(onPhoto: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val c = Palette.colors
    val ink = if (onPhoto) Color.White else c.ink
    val paper = if (onPhoto) Color.Black else c.paper
    val interaction = remember { MutableInteractionSource() }
    Box(
        modifier.fillMaxHeight().pressScale(interaction, 0.9f).clickable(interaction, indication = null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Box(Modifier.size(24.dp).clip(CircleShape).background(ink), contentAlignment = Alignment.Center) {
                Icon(OrielIcons.Download, "Install the update", tint = paper, modifier = Modifier.size(14.dp))
            }
            Spacer(Modifier.height(1.dp))
            Text("Install", style = MaterialTheme.typography.labelSmall.copy(letterSpacing = 0.sp), color = ink,
                fontWeight = FontWeight.SemiBold, maxLines = 1)
        }
    }
}

@Composable
private fun NavItem(tab: Tab, selected: Boolean, onPhoto: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val c = Palette.colors
    val ink = if (onPhoto) Color.White else c.ink
    val fg by animateColorAsState(if (selected) ink else ink.copy(alpha = 0.6f), tween(220), label = "navFg")
    val interaction = remember { MutableInteractionSource() }

    val scale = remember { Animatable(1f) }
    val rotation = remember { Animatable(0f) }
    val lift = remember { Animatable(0f) }
    val flip = remember { Animatable(0f) }
    // The animation runs in a scope of its own: picking another tab mid-way must not freeze the icon half hopped or
    // half turned. It plays to the end and comes to rest.
    val play = rememberCoroutineScope()
    LaunchedEffect(selected) {
        if (!selected) return@LaunchedEffect
        play.launch { when (tab.animation) {
            Play.Hop -> {
                launch { lift.animateTo(0f, keyframes { durationMillis = 520; -7f at 160; 0f at 320; -2f at 420 }) }
                scale.animateTo(1f, keyframes { durationMillis = 520; 1.15f at 160; 0.94f at 320; 1.03f at 420 })
            }
            // A compass finding north: a little pull back, a full swing round, overshoot, settle.
            Play.Compass -> {
                launch { scale.animateTo(1f, keyframes { durationMillis = 520; 1.15f at 180 }) }
                rotation.snapTo(0f)
                rotation.animateTo(360f, keyframes { durationMillis = 920; -25f at 120; 385f at 520; 350f at 680; 362f at 820 })
                rotation.snapTo(0f)
            }
            // The clock winds back: one full turn anticlockwise, like rewinding time.
            Play.Flip -> {
                rotation.snapTo(0f)
                rotation.animateTo(-360f, tween(750, easing = FastOutSlowInEasing))
                rotation.snapTo(0f)
            }
            Play.Spin -> {
                rotation.snapTo(0f)
                rotation.animateTo(180f, spring(dampingRatio = 0.5f, stiffness = 110f))
                rotation.snapTo(0f)
            }
        } }
    }

    Box(
        modifier.fillMaxHeight().pressScale(interaction, 0.9f).clickable(interaction, indication = null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Crossfade(selected, animationSpec = tween(180), label = "navIcon") { on ->
                Icon(
                    if (on) tab.selectedIcon else tab.icon, tab.label, tint = fg,
                    modifier = Modifier.size(22.dp).graphicsLayer {
                        scaleX = scale.value; scaleY = scale.value
                        rotationZ = rotation.value
                        translationY = lift.value * density
                        rotationX = flip.value
                        cameraDistance = 12 * density
                    },
                )
            }
            Spacer(Modifier.height(3.dp))
            Text(tab.label, style = MaterialTheme.typography.labelSmall.copy(letterSpacing = 0.sp), color = fg,
                fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium, maxLines = 1)
        }
    }
}
