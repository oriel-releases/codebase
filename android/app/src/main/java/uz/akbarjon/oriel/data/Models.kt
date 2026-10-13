package uz.akbarjon.oriel.data

/** An Unsplash photo, before or after it was used. */
data class Photo(
    val id: String,
    val width: Int,
    val height: Int,
    val likes: Int,
    val description: String?,
    /** The photo's average color, "#RRGGBB". */
    val color: String?,
    /** Accepts imgix sizing params; every device makes its own crop from it. */
    val rawUrl: String,
    val pageUrl: String,
    val photographer: String,
    val photographerUrl: String,
    /** Pinged when the photo is used (Unsplash API guidelines). */
    val downloadLocation: String?,
) {
    /**
     * A [widthPx]-wide image cropped to [heightPx] tall (or kept in proportion when null), around [position]: Unsplash's
     * CDN crops it, so the phone gets exactly the pixels it shows.
     */
    fun sized(widthPx: Int, heightPx: Int? = null, quality: Int = 100, format: String = "jpg", position: Position = Position.Smart): String {
        val sep = if ('?' in rawUrl) '&' else '?'
        val crop = if (heightPx == null) "" else "&h=$heightPx&fit=crop" + when (position) {
            Position.Smart -> "&crop=faces,entropy"
            Position.Center -> ""
            is Position.Focus -> String.format(java.util.Locale.US, "&crop=focalpoint&fp-x=%.3f&fp-y=%.3f", position.x, position.y)
        }
        return "$rawUrl${sep}w=$widthPx$crop&q=$quality&fm=$format"
    }

    /** Small WebP preview (a few KB), cropped to the phone's shape when [heightPx] is given. */
    fun thumb(widthPx: Int, heightPx: Int? = null, position: Position = Position.Smart) =
        sized(widthPx, heightPx, quality = 90, format = "webp", position = position)
}

/**
 * Where a photo is cropped to the phone's shape: Unsplash's smart crop (faces, then detail), the middle, or the point
 * the user chose in the position editor (0..1 across and down the photo). Only this phone's; not synced.
 */
sealed interface Position {
    data object Smart : Position
    data object Center : Position
    data class Focus(val x: Float, val y: Float) : Position
}

/** A wallpaper Oriel applied. Only metadata is kept: the image can always be fetched again from [Photo.rawUrl]. */
data class HistoryEntry(
    val id: Long,
    val photo: Photo,
    val topic: String?,
    val appliedAt: Long,
    val isFavorite: Boolean,
)
