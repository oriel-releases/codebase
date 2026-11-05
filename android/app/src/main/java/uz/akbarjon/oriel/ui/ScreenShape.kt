package uz.akbarjon.oriel.ui

import android.hardware.display.DisplayManager
import android.os.Build
import android.util.DisplayMetrics
import android.view.Display
import android.view.RoundedCorner
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.RoundRect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Outline
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.LayoutDirection
import uz.akbarjon.oriel.Oriel

/**
 * The phone's own screen corners, as a share of the screen's width: every phone-shaped picture (Home's frame, the tiles,
 * the photo page, the crop window) is a small copy of this screen, so its corners are the screen's, scaled down.
 */
object ScreenCorners {
    /** For phones that don't say (Android 11 and older, or square screens): about what most phones have. */
    private const val FALLBACK = 0.1f

    val ratio: Float by lazy { measure() ?: FALLBACK }

    /** The corner radius of a phone-shaped picture [width] wide (any unit). */
    fun of(width: Float): Float = width * ratio

    private fun measure(): Float? {
        if (Build.VERSION.SDK_INT < 31) return null
        val display = Oriel.app.getSystemService(DisplayManager::class.java).getDisplay(Display.DEFAULT_DISPLAY) ?: return null
        val radius = display.getRoundedCorner(RoundedCorner.POSITION_TOP_LEFT)?.radius ?: return null
        val metrics = DisplayMetrics()
        @Suppress("DEPRECATION")
        display.getRealMetrics(metrics)
        val width = minOf(metrics.widthPixels, metrics.heightPixels)
        if (radius <= 0 || width <= 0) return null
        return radius.toFloat() / width
    }
}

/** A phone-shaped picture's outline: rounded like this phone's screen, at whatever size it's drawn. */
object ScreenShape : Shape {
    override fun createOutline(size: Size, layoutDirection: LayoutDirection, density: Density): Outline {
        val r = ScreenCorners.of(size.width)
        return Outline.Rounded(RoundRect(0f, 0f, size.width, size.height, CornerRadius(r, r)))
    }
}

/** The corner radius of a phone-shaped picture [width] wide. */
fun screenCorner(width: Dp): Dp = width * ScreenCorners.ratio
