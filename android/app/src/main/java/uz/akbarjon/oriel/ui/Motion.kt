package uz.akbarjon.oriel.ui

import androidx.compose.animation.AnimatedContentTransitionScope
import androidx.compose.animation.ContentTransform
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.animation.core.FiniteAnimationSpec
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith

/**
 * How Oriel moves, everywhere: things leave quickly and arrive slowly, easing in and out (Material's emphasized
 * curves), so a change feels soft instead of a cut. Only opacity, scale and position move: nothing that costs a frame.
 */
object Motion {
    /** In and out together: a quick start, a long soft landing. */
    val Emphasized = CubicBezierEasing(0.2f, 0f, 0f, 1f)
    /** Arriving: already moving, then settling slowly. */
    val Decelerate = CubicBezierEasing(0.05f, 0.7f, 0.1f, 1f)
    /** Leaving: starts slowly, then goes. */
    val Accelerate = CubicBezierEasing(0.3f, 0f, 0.8f, 0.15f)

    const val SHORT = 180
    const val MEDIUM = 340
    const val LONG = 520

    fun <T> enter(ms: Int = MEDIUM, delay: Int = 0): FiniteAnimationSpec<T> = tween(ms, delay, Decelerate)
    fun <T> exit(ms: Int = SHORT): FiniteAnimationSpec<T> = tween(ms, easing = Accelerate)
    fun <T> move(ms: Int = MEDIUM): FiniteAnimationSpec<T> = tween(ms, easing = Emphasized)

    /** Ease in and out: gentle at both ends (the sheets rising from the bottom). */
    val InOut = CubicBezierEasing(0.65f, 0f, 0.35f, 1f)

    /** One thing in place of another (a tab, a step): the new one fades and grows in a touch, the old one fades away. */
    fun <S> AnimatedContentTransitionScope<S>.fadeThrough(): ContentTransform =
        (fadeIn(enter(MEDIUM, delay = 60)) + scaleIn(enter(LONG), initialScale = 0.96f)) togetherWith
            (fadeOut(exit()) + scaleOut(exit(), targetScale = 1.02f))

    /**
     * Going deeper (a sub page) or back, or to the next of a few side by side (tabs, steps): the whole page slides the
     * way you went, the old one out the other side. No fading.
     */
    fun <S> AnimatedContentTransitionScope<S>.slideThrough(forward: Boolean): ContentTransform =
        slideInHorizontally(tween(SLIDE, easing = Decelerate)) { if (forward) it else -it } togetherWith
            slideOutHorizontally(tween(SLIDE, easing = Decelerate)) { if (forward) -it else it }

    /** A page sliding in or out: short, so it stays smooth even while the new page is still being built. */
    const val SLIDE = 300
}
