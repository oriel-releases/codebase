package uz.akbarjon.oriel.ui

import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.util.VelocityTracker
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.unit.dp
import kotlin.math.abs

/**
 * A sideways swipe that moves one step, like the buttons beside it do: to the left is "next" (+1), to the right is
 * "back" (-1). It counts once the finger has gone far enough (a fifth of the width) or flicked fast, and only in a
 * direction [can] allows; a light tick confirms it. Vertical scrolling inside keeps working: only a mostly sideways
 * drag is taken.
 */
fun Modifier.swipeSteps(can: (Int) -> Boolean, onStep: (Int) -> Unit): Modifier = composed {
    val haptics = LocalHapticFeedback.current
    val allowed = rememberUpdatedState(can)
    val step = rememberUpdatedState(onStep)
    pointerInput(Unit) {
        val fling = 900.dp.toPx()
        var total = 0f
        val tracker = VelocityTracker()
        detectHorizontalDragGestures(
            onDragStart = { total = 0f; tracker.resetTracking() },
            onDragEnd = {
                val speed = tracker.calculateVelocity().x
                val dir = when {
                    total < -size.width / 5f || speed < -fling -> 1
                    total > size.width / 5f || speed > fling -> -1
                    else -> 0
                }
                if (dir != 0 && abs(total) > 24.dp.toPx() && allowed.value(dir)) {
                    haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
                    step.value(dir)
                }
            },
        ) { change, amount ->
            total += amount
            tracker.addPosition(change.uptimeMillis, change.position)
        }
    }
}
