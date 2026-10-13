package uz.akbarjon.oriel.core

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import java.io.File
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * The photo's accent color, the same pick as the Windows app's `AccentPicker`: its most striking hue, adjusted so it
 * works as a UI color. The app tints itself with it, and it goes to `sync/current.color` for the extension.
 */
object AccentPicker {
    private const val HUE_BUCKETS = 24

    /** From the downloaded wallpaper, decoded at a tiny size (a few thousand pixels are plenty). Null if it can't be read. */
    fun fromFile(file: File): String? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(file.path, bounds)
        if (bounds.outWidth <= 0) return null
        var sample = 1
        while (bounds.outWidth / (sample * 2) >= 48) sample *= 2
        val bitmap = BitmapFactory.decodeFile(file.path, BitmapFactory.Options().apply {
            inSampleSize = sample
            inPreferredConfig = Bitmap.Config.ARGB_8888
        }) ?: return null
        val pixels = IntArray(bitmap.width * bitmap.height)
        bitmap.getPixels(pixels, 0, bitmap.width, 0, 0, bitmap.width, bitmap.height)
        bitmap.recycle()
        return toHex(pick(pixels))
    }

    /** Unsplash's average color made usable as an accent: for photos set before the app picked accents itself. */
    fun fromAverage(hex: String?): String? {
        val c = hex?.takeIf { it.length == 7 && it[0] == '#' }?.substring(1)?.toIntOrNull(16) ?: return null
        val (h, s, v) = hsv((c shr 16 and 0xFF).toDouble(), (c shr 8 and 0xFF).toDouble(), (c and 0xFF).toDouble())
        return toHex(fromHsv(h, if (s < 0.08) s else s.coerceAtLeast(0.45), v.coerceIn(0.55, 0.85)))
    }

    private fun pick(argb: IntArray): Int {
        val weight = DoubleArray(HUE_BUCKETS)
        val sumR = DoubleArray(HUE_BUCKETS)
        val sumG = DoubleArray(HUE_BUCKETS)
        val sumB = DoubleArray(HUE_BUCKETS)
        var allR = 0.0
        var allG = 0.0
        var allB = 0.0
        for (p in argb) {
            val r = (p shr 16 and 0xFF).toDouble()
            val g = (p shr 8 and 0xFF).toDouble()
            val b = (p and 0xFF).toDouble()
            allR += r; allG += g; allB += b
            val (h, s, v) = hsv(r, g, b)
            // Skip near-black, near-white and washed-out pixels: they make dull accents.
            if (v < 0.2 || s < 0.25 || (v > 0.95 && s < 0.4)) continue
            val bucket = minOf(HUE_BUCKETS - 1, (h / 360 * HUE_BUCKETS).toInt())
            val w = s * s * v   // favor saturated, bright pixels
            weight[bucket] += w
            sumR[bucket] += r * w; sumG[bucket] += g * w; sumB[bucket] += b * w
        }
        val best = weight.indices.maxBy { weight[it] }
        val n = argb.size
        val (mr, mg, mb) = when {
            weight[best] > 0 -> Triple(sumR[best] / weight[best], sumG[best] / weight[best], sumB[best] / weight[best])
            n > 0 -> Triple(allR / n, allG / n, allB / n)   // a grayscale photo: use its average
            else -> return 0x0078D4
        }
        var (h, s, v) = hsv(mr, mg, mb)
        if (weight[best] > 0) s = s.coerceAtLeast(0.55)
        v = v.coerceIn(0.55, 0.85)
        return fromHsv(h, s, v)
    }

    private fun toHex(rgb: Int) = "#%06X".format(rgb and 0xFFFFFF)

    private fun hsv(r0: Double, g0: Double, b0: Double): Triple<Double, Double, Double> {
        val r = r0 / 255; val g = g0 / 255; val b = b0 / 255
        val max = maxOf(r, g, b)
        val d = max - minOf(r, g, b)
        val h = when {
            d == 0.0 -> 0.0
            max == r -> 60 * (((g - b) / d % 6 + 6) % 6)
            max == g -> 60 * ((b - r) / d + 2)
            else -> 60 * ((r - g) / d + 4)
        }
        return Triple(h, if (max == 0.0) 0.0 else d / max, max)
    }

    private fun fromHsv(h: Double, s: Double, v: Double): Int {
        val c = v * s
        val x = c * (1 - abs(h / 60 % 2 - 1))
        val m = v - c
        val (r, g, b) = when ((h / 60).toInt()) {
            0 -> Triple(c, x, 0.0)
            1 -> Triple(x, c, 0.0)
            2 -> Triple(0.0, c, x)
            3 -> Triple(0.0, x, c)
            4 -> Triple(x, 0.0, c)
            else -> Triple(c, 0.0, x)
        }
        fun ch(d: Double) = ((d + m) * 255).roundToInt().coerceIn(0, 255)
        return (ch(r) shl 16) or (ch(g) shl 8) or ch(b)
    }
}
