package uz.akbarjon.oriel.ui

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
internal enum class Play { Hop, Wiggle, Beat, Flip, Spin }

enum class Tab(val label: String, val icon: ImageVector, val selectedIcon: ImageVector, internal val animation: Play) {
    Home("Home", OrielIcons.Home, OrielIcons.HomeFilled, Play.Hop),
    Search("Search", OrielIcons.Search, OrielIcons.Search, Play.Wiggle),
    Create("AI", OrielIcons.Sparkles, OrielIcons.SparklesFilled, Play.Beat),
    History("History", OrielIcons.History, OrielIcons.History, Play.Flip),
    Settings("Settings", OrielIcons.Settings, OrielIcons.Settings, Play.Spin),
}

/**
 * The navigation bar: a floating capsule over the content. One soft pill slides to the picked tab, stretching like
 * a drop of water (its leading edge moves first, the trailing edge catches up), and the picked tab's icon plays a little
 * animation: the house hops, the magnifier wiggles, the clock winds back, the friends pulse, the gear spins. Over Home's photo
 * it turns dark and see-through ([onPhoto]). With the phone on its side ([vertical]) it stands on the right edge.
 */
@Composable
fun NavBar(
    selected: Tab, onSelect: (Tab) -> Unit, haze: HazeState, modifier: Modifier = Modifier, onPhoto: Boolean = false,
    vertical: Boolean = false,
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
        modifier.navigationBarsPadding().padding(horizontal = 16.dp, vertical = NavBarMargin).fillMaxWidth().height(NavBarHeight)
    }
    Box(frame) {
    // Only the bar's background is clipped to the capsule; the icons draw on top, free to hop and flip past its edge.
    Box(
        Modifier.matchParentSize()
            .shadow(if (onPhoto) 0.dp else 18.dp, shape, clip = false, ambientColor = Color.Black.copy(alpha = 0.18f), spotColor = Color.Black.copy(alpha = 0.18f))
            .clip(shape)
            .hazeEffect(haze, HazeStyle(backgroundColor = if (onPhoto) Color.Black else c.paper, tint = HazeTint(tint), blurRadius = 24.dp, noiseFactor = 0f))
            .border(1.dp, edge, shape),
    )
    BoxWithConstraints(Modifier.fillMaxSize().padding(6.dp)) {
        val itemSize = (if (vertical) maxHeight else maxWidth) / Tab.entries.size
        val index = selected.ordinal
        var from by remember { mutableIntStateOf(index) }
        val forward = index >= from
        val fast = spring<Dp>(dampingRatio = 0.75f, stiffness = 900f)
        val slow = spring<Dp>(dampingRatio = 0.7f, stiffness = 260f)
        val start by animateDpAsState(itemSize * index, if (forward) slow else fast, label = "pillStart")
        val end by animateDpAsState(itemSize * (index + 1), if (forward) fast else slow, label = "pillEnd")
        LaunchedEffect(index) { from = index }
        Box(
            if (vertical) Modifier.offset(y = start).height(end - start).fillMaxWidth().clip(shape).background(pillColor)
            else Modifier.offset(x = start).width(end - start).fillMaxHeight().clip(shape).background(pillColor),
        )

        val pick = { tab: Tab ->
            if (tab != selected) haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
            onSelect(tab)
        }
        if (vertical) {
            Column(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally) {
                Tab.entries.forEach { tab -> NavItem(tab, tab == selected, onPhoto, Modifier.weight(1f).fillMaxWidth()) { pick(tab) } }
            }
        } else {
            Row(Modifier.fillMaxSize(), verticalAlignment = Alignment.CenterVertically) {
                Tab.entries.forEach { tab -> NavItem(tab, tab == selected, onPhoto, Modifier.weight(1f)) { pick(tab) } }
            }
        }
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
            Play.Wiggle -> rotation.animateTo(0f, keyframes { durationMillis = 560; -20f at 110; 16f at 240; -8f at 360; 4f at 460 })
            Play.Beat -> scale.animateTo(1f, keyframes { durationMillis = 620; 1.28f at 120; 0.95f at 240; 1.15f at 360; 1f at 480 })
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
