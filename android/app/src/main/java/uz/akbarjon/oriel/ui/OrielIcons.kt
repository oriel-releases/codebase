package uz.akbarjon.oriel.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.unit.dp

/**
 * Oriel's icons: thin, rounded line icons on a 24-unit grid (shapes from Lucide, ISC license), drawn in code so no
 * icon library ships. Material's default icons look heavy and dated next to them. `filled` variants are for the
 * picked tab.
 */
object OrielIcons {
    private const val STROKE = 1.75f

    /** Path shorthand: `c(cx, cy, r)` circles and `r(x, y, w, h, rx)` rounded rects become plain path data. */
    private fun c(cx: Float, cy: Float, r: Float) = "M${cx - r},$cy a$r,$r 0 1,0 ${2 * r},0 a$r,$r 0 1,0 ${-2 * r},0"

    private fun r(x: Float, y: Float, w: Float, h: Float, rx: Float) =
        "M${x + rx},$y h${w - 2 * rx} a$rx,$rx 0 0 1 $rx,$rx v${h - 2 * rx} a$rx,$rx 0 0 1 -$rx,$rx h${-(w - 2 * rx)} " +
            "a$rx,$rx 0 0 1 -$rx,-$rx v${-(h - 2 * rx)} a$rx,$rx 0 0 1 $rx,-$rx z"

    private fun icon(name: String, vararg paths: String, fill: Set<Int> = emptySet()): ImageVector =
        ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f).apply {
            paths.forEachIndexed { i, d ->
                addPath(
                    PathParser().parsePathString(d).toNodes(),
                    fill = if (i in fill) SolidColor(Color.Black) else null,
                    stroke = SolidColor(Color.Black), strokeLineWidth = STROKE,
                    strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round,
                )
            }
        }.build()

    private const val HOUSE = "M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"
    private const val HEART = "M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z"
    private const val STAR = "M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z"
    private const val GEAR = "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"

    // Tabs
    val Home = icon("Home", HOUSE, "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8")
    val HomeFilled = icon("HomeFilled", HOUSE, "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8", fill = setOf(0))
    val Search = icon("Search", c(11f, 11f, 8f), "m21 21-4.3-4.3")
    val Heart = icon("Heart", HEART)
    val HeartFilled = icon("HeartFilled", HEART, fill = setOf(0))
    val History = icon("History", "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", "M3 3v5h5", "M12 7v5l4 2")
    val Settings = icon("Settings", GEAR, c(12f, 12f, 3f))
    val Sparkles = icon("Sparkles",
        "M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z",
        "M20 3v4", "M22 5h-4", "M4 17v2", "M5 18H3")
    val SparklesFilled = icon("SparklesFilled",
        "M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z",
        "M20 3v4", "M22 5h-4", "M4 17v2", "M5 18H3", fill = setOf(0))
    val ArrowUp = icon("ArrowUp", "m5 12 7-7 7 7", "M12 19V5")
    val Dice = icon("Dice", r(3f, 3f, 18f, 18f, 2f), c(8f, 8f, 0.6f), c(16f, 8f, 0.6f), c(12f, 12f, 0.6f), c(8f, 16f, 0.6f), c(16f, 16f, 0.6f),
        fill = setOf(1, 2, 3, 4, 5))

    // Actions and rows
    val Refresh = icon("Refresh", "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8", "M21 3v5h-5",
        "M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16", "M8 16H3v5")
    val Calendar = icon("Calendar", "M8 2v4", "M16 2v4", r(3f, 4f, 18f, 18f, 2f), "M3 10h18")
    val Clock = icon("Clock", c(12f, 12f, 10f), "M12 6v6l4 2")
    val Globe = icon("Globe", c(12f, 12f, 10f), "M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20", "M2 12h20")
    val Phone = icon("Phone", r(5f, 2f, 14f, 20f, 2f), "M12 18h.01")
    val Monitor = icon("Monitor", r(2f, 3f, 20f, 14f, 2f), "M8 21h8", "M12 17v4")
    val Image = icon("Image", r(3f, 3f, 18f, 18f, 2f), c(9f, 9f, 2f), "m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21")
    val Lock = icon("Lock", r(3f, 11f, 18f, 11f, 2f), "M7 11V7a5 5 0 0 1 10 0v4")
    val Unlock = icon("Unlock", r(3f, 11f, 18f, 11f, 2f), "M7 11V7a5 5 0 0 1 9.9-1")
    val Share = icon("Share", c(18f, 5f, 3f), c(6f, 12f, 3f), c(18f, 19f, 3f), "m8.59 13.51 6.83 3.98", "m15.41 6.51-6.82 3.98")
    val LogOut = icon("LogOut", "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4", "m16 17 5-5-5-5", "M21 12H9")
    val Info = icon("Info", c(12f, 12f, 10f), "M12 16v-4", "M12 8h.01")
    val Alert = icon("Alert", c(12f, 12f, 10f), "M12 8v4", "M12 16h.01")
    val Send = icon("Send", "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z", "m21.854 2.147-10.94 10.939")
    val Shield = icon("Shield", "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z")
    val Trending = icon("Trending", "M22 7 13.5 15.5 8.5 10.5 2 17", "M16 7h6v6")
    val Star = icon("Star", STAR)
    val StarFilled = icon("StarFilled", STAR, fill = setOf(0))
    val User = icon("User", "M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2", c(12f, 7f, 4f))
    val Users = icon("Users", "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2", c(9f, 7f, 4f), "M22 21v-2a4 4 0 0 0-3-3.87", "M16 3.13a4 4 0 0 1 0 7.75")
    val Crop = icon("Crop", "M6 2v14a2 2 0 0 0 2 2h14", "M18 22V8a2 2 0 0 0-2-2H2")
    val Move = icon("Move", "m18 8 4 4-4 4", "M2 12h20", "m6 8-4 4 4 4")
    val Copy = icon("Copy", r(8f, 8f, 14f, 14f, 2f), "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2")
    val Plus = icon("Plus", "M5 12h14", "M12 5v14")
    val Close = icon("Close", "M18 6 6 18", "m6 6 12 12")
    val Check = icon("Check", "M20 6 9 17l-5-5")
    val ChevronRight = icon("ChevronRight", "m9 18 6-6-6-6")
    val Trash = icon("Trash", "M3 6h18", "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2")
    val Mail = icon("Mail", r(2f, 4f, 20f, 16f, 2f), "m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7")
    val Pencil = icon("Pencil", "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z", "m15 5 4 4")
    val Layers = icon("Layers", "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z",
        "M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12", "M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17")
}
