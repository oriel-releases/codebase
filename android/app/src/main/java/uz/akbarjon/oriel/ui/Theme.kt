package uz.akbarjon.oriel.ui

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.AccentPicker

/**
 * Oriel's palette is black and white: paper, white surfaces, hairlines, muted and ink text. The **accent** comes from the
 * current wallpaper (like the Windows app's accent color) and is used only as a faint wash behind the photo on Home.
 */
@Immutable
data class OrielColors(
    val paper: Color, val surface: Color, val raised: Color, val line: Color, val muted: Color, val ink: Color,
    val accent: Color = DefaultAccent, val onAccent: Color = Color.White, val dark: Boolean = false,
) {
    /** The accent as a quiet tint, for selected things. */
    val accentSoft get() = accent.copy(alpha = if (dark) 0.20f else 0.12f)
    /** Dims what's behind a sheet. */
    val scrim get() = Color.Black.copy(alpha = if (dark) 0.6f else 0.32f)
}

private val DefaultAccent = Color(0xFF5B6CF0)

/** Favorites are a gold star, the same as the desktop apps (OrielStarBrush). */
val StarGold = Color(0xFFF5B301)

private val Light = OrielColors(
    paper = Color(0xFFF4F4F6), surface = Color(0xFFFFFFFF), raised = Color(0xFFFFFFFF), line = Color(0xFFE7E7EB),
    muted = Color(0xFF8A8A92), ink = Color(0xFF111114),
)
private val Dark = OrielColors(
    paper = Color(0xFF09090B), surface = Color(0xFF151518), raised = Color(0xFF1D1D21), line = Color(0xFF26262B),
    muted = Color(0xFF8E8E96), ink = Color(0xFFF4F4F6), dark = true,
)

private val LocalOrielColors = staticCompositionLocalOf { Light }

object Palette {
    val colors: OrielColors
        @Composable @ReadOnlyComposable get() = LocalOrielColors.current
}

/** The current wallpaper's accent: picked when it was set, else made from the last photo's average color. */
@Composable
private fun wallpaperAccent(): Color {
    val state by Oriel.store.state.collectAsState()
    val version by Oriel.history.version.collectAsState()
    val fallback by produceState<String?>(null, version) { value = AccentPicker.fromAverage(Oriel.history.latest()?.photo?.color) }
    return parseColor(state.accent ?: fallback) ?: DefaultAccent
}

@Composable
fun OrielTheme(content: @Composable () -> Unit) {
    val base = if (isSystemInDarkTheme()) Dark else Light
    val accent by animateColorAsState(wallpaperAccent(), tween(900), label = "accent")
    val c = base.copy(accent = accent, onAccent = if (accent.luminance() > 0.55f) Color(0xFF17171A) else Color.White)
    val scheme = (if (c.dark) darkColorScheme() else lightColorScheme()).copy(
        // Black and white: Material's own controls (progress, cursors, pickers) draw in ink, not the accent.
        primary = c.ink, onPrimary = c.paper,
        secondary = c.ink, onSecondary = c.paper,
        secondaryContainer = c.line, onSecondaryContainer = c.ink,
        primaryContainer = c.line, onPrimaryContainer = c.ink,
        tertiary = c.ink, onTertiary = c.paper,
        background = c.paper, onBackground = c.ink,
        surface = c.surface, onSurface = c.ink,
        surfaceVariant = c.paper, onSurfaceVariant = c.muted,
        surfaceContainer = c.surface, surfaceContainerLow = c.surface, surfaceContainerHigh = c.raised,
        surfaceContainerHighest = c.raised, surfaceContainerLowest = c.paper, surfaceTint = Color.Transparent,
        inverseSurface = c.ink, inverseOnSurface = c.paper, scrim = Color.Black,
        outline = c.line, outlineVariant = c.line,
        error = Color(0xFFE5484D),
    )
    val type = Typography(
        displaySmall = TextStyle(fontSize = 34.sp, lineHeight = 40.sp, fontWeight = FontWeight.Bold, letterSpacing = (-1).sp),
        headlineMedium = TextStyle(fontSize = 32.sp, lineHeight = 38.sp, fontWeight = FontWeight.Bold, letterSpacing = (-1).sp),
        titleLarge = TextStyle(fontSize = 22.sp, lineHeight = 28.sp, fontWeight = FontWeight.SemiBold, letterSpacing = (-0.4).sp),
        titleMedium = TextStyle(fontSize = 17.sp, lineHeight = 22.sp, fontWeight = FontWeight.SemiBold, letterSpacing = (-0.2).sp),
        bodyLarge = TextStyle(fontSize = 16.sp, lineHeight = 22.sp, letterSpacing = (-0.1).sp),
        bodyMedium = TextStyle(fontSize = 14.sp, lineHeight = 20.sp),
        bodySmall = TextStyle(fontSize = 12.sp, lineHeight = 16.sp),
        labelLarge = TextStyle(fontSize = 15.sp, lineHeight = 20.sp, fontWeight = FontWeight.SemiBold),
        labelMedium = TextStyle(fontSize = 12.sp, lineHeight = 16.sp, fontWeight = FontWeight.SemiBold),
        labelSmall = TextStyle(fontSize = 10.sp, lineHeight = 12.sp, fontWeight = FontWeight.Bold, letterSpacing = 0.8.sp),
    )
    CompositionLocalProvider(LocalOrielColors provides c) {
        MaterialTheme(colorScheme = scheme, typography = type) {
            // Text and icons without a color of their own are ink (not Compose's default black, lost in dark mode).
            CompositionLocalProvider(LocalContentColor provides c.ink, content = content)
        }
    }
}

/** "#AABBCC" from Unsplash as a Color, or null. */
fun parseColor(hex: String?): Color? = try {
    hex?.takeIf { it.length == 7 && it[0] == '#' }?.let { Color(android.graphics.Color.parseColor(it)) }
} catch (_: IllegalArgumentException) {
    null
}
