package uz.akbarjon.oriel.ui

import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.Alignment
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.LruCache
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import uz.akbarjon.oriel.data.Http

/**
 * A few lines instead of an image library: Unsplash's CDN already sends each thumbnail at the exact size it is shown
 * (WebP), the HTTP cache keeps it on disk, and decoded bitmaps sit in a small memory cache in full color (no banding
 * in skies and gradients).
 */
private object Thumbnails {
    private val memory = object : LruCache<String, ImageBitmap>(
        (Runtime.getRuntime().maxMemory() / 10).toInt().coerceAtMost(48 * 1024 * 1024),
    ) {
        override fun sizeOf(key: String, value: ImageBitmap) = value.width * value.height * 4
    }
    private val gate = Semaphore(4)

    fun cached(url: String): ImageBitmap? = memory.get(url)

    suspend fun load(url: String): ImageBitmap? = memory.get(url) ?: gate.withPermit {
        memory.get(url) ?: try {
            val bytes = Http.bytes(url)
            withContext(Dispatchers.Default) {
                val options = BitmapFactory.Options().apply { inPreferredConfig = Bitmap.Config.ARGB_8888 }
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size, options)?.asImageBitmap()
            }?.also { memory.put(url, it) }
        } catch (_: Exception) {
            null
        }
    }
}

/** A photo from the network as a bitmap (through the same caches), for screens that draw it themselves. */
suspend fun loadBitmap(url: String): ImageBitmap? = Thumbnails.load(url)

/**
 * A photo from the network, over its own color until it arrives. [focus] (0..1 across and down) is the point the crop is
 * centered on, kept inside the photo: the same rule the phone uses to crop an AI wallpaper ([Wallpapers.fetch]), so a
 * preview shows exactly what the screen does. Without it the middle shows.
 */
@Composable
fun RemoteImage(
    url: String?, contentDescription: String?, modifier: Modifier = Modifier, placeholder: Color = Palette.colors.surface,
    focus: Pair<Float, Float>? = null,
) {
    val bitmap by produceState(url?.let { Thumbnails.cached(it) }, url) {
        value = url?.let { Thumbnails.cached(it) }
        if (value == null && url != null) value = Thumbnails.load(url)
    }
    // Only fade in what had to be fetched; a cached image shows at once.
    val alpha by animateFloatAsState(if (bitmap != null) 1f else 0f, tween(220), label = "fade")
    Box(modifier.background(placeholder)) {
        bitmap?.let {
            Image(it, contentDescription, Modifier.matchParentSize().alpha(alpha), contentScale = ContentScale.Crop,
                alignment = focus?.let { (fx, fy) -> FocusAlignment(fx, fy) } ?: Alignment.Center)
        }
    }
}

/** Puts the photo's point ([x], [y], 0..1) in the middle of the frame, as far as the photo's edges allow. */
private class FocusAlignment(private val x: Float, private val y: Float) : Alignment {
    override fun align(size: IntSize, space: IntSize, layoutDirection: LayoutDirection): IntOffset {
        val ox = (size.width * x - space.width / 2f).coerceIn(0f, (size.width - space.width).coerceAtLeast(0).toFloat())
        val oy = (size.height * y - space.height / 2f).coerceIn(0f, (size.height - space.height).coerceAtLeast(0).toFloat())
        return IntOffset(-ox.toInt(), -oy.toInt())
    }
}
