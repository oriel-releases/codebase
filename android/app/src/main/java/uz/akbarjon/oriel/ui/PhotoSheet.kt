package uz.akbarjon.oriel.ui

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.keyframes
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.ChangeResult
import uz.akbarjon.oriel.data.Photo

/** Sets [photo] as the wallpaper and says how it went. Keeps going if the screen that asked goes away. */
fun setAsWallpaper(photo: Photo, topic: String?, snackbar: SnackbarHostState, onDone: () -> Unit = {}) {
    Oriel.scope.launch(Dispatchers.Main) {
        val result = Oriel.wallpapers.apply(photo, topic)
        if (result is ChangeResult.Applied) onDone()
        snackbar.showSnackbar(
            when (result) {
                is ChangeResult.Applied -> "Wallpaper set"
                is ChangeResult.Failed -> result.message
                ChangeResult.Skipped -> "Nothing changed"
            },
        )
    }
}

/**
 * A photo up close, cropped to this phone's shape the way it will look as the wallpaper, with its credit.
 * History passes [favorite] and the delete action; Search and shared links don't.
 */
@Composable
fun PhotoSheet(
    photo: Photo,
    topic: String?,
    onDismiss: () -> Unit,
    heading: String? = null,
    favorite: Boolean? = null,
    onFavorite: ((Boolean) -> Unit)? = null,
    onDelete: (() -> Unit)? = null,
) {
    val context = LocalContext.current
    val snackbar = LocalSnackbar.current
    val haptics = LocalHapticFeedback.current
    val busy by Oriel.wallpapers.busy.collectAsState()
    val phase by Oriel.wallpapers.phase.collectAsState()
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    val previewWidthPx = with(LocalDensity.current) { 220.dp.roundToPx() }

    OrielSheet(onDismiss, heading) {
        Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
            RemoteImage(
                photo.thumb(previewWidthPx, previewWidthPx * screenH / screenW, uz.akbarjon.oriel.core.Positions.of(photo.id)), photo.description,
                Modifier.width(220.dp).aspectRatio(screenW.toFloat() / screenH).clip(RoundedCornerShape(28.dp)),
                placeholder = parseColor(photo.color) ?: Palette.colors.surface,
            )
            Spacer(Modifier.height(16.dp))
            photo.description?.let {
                Text(it.replaceFirstChar { c -> c.uppercase() }, style = MaterialTheme.typography.bodyLarge, maxLines = 2,
                    overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(horizontal = 24.dp))
                Spacer(Modifier.height(4.dp))
            }
            Credit(photo, context, Modifier.padding(horizontal = 24.dp))
        }
        Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 20.dp), horizontalArrangement = Arrangement.spacedBy(10.dp),
            verticalAlignment = Alignment.CenterVertically) {
            PrimaryButton(if (busy) phase?.text ?: "Setting…" else "Set as wallpaper", { setAsWallpaper(photo, topic, snackbar, onDismiss) },
                Modifier.weight(1f), busy = busy, icon = { Icon(OrielIcons.Check, null, Modifier.size(20.dp)) })
            if (favorite != null && onFavorite != null) {
                // The heart pops when it's filled.
                val pop = remember { Animatable(1f) }
                LaunchedEffect(favorite) {
                    if (favorite) pop.animateTo(1f, keyframes { durationMillis = 450; 1.35f at 120; 0.9f at 260 })
                }
                RoundIconButton(
                    if (favorite) OrielIcons.StarFilled else OrielIcons.Star,
                    if (favorite) "Remove from favorites" else "Add to favorites",
                    onClick = { haptics.performHapticFeedback(HapticFeedbackType.Confirm); onFavorite(!favorite) },
                    modifier = Modifier.graphicsLayer { scaleX = pop.value; scaleY = pop.value },
                )
            }
            RoundIconButton(OrielIcons.Share, "Share this wallpaper", {
                Oriel.scope.launch(Dispatchers.Main) {
                    try {
                        shareText(context, "A wallpaper for you: ${uz.akbarjon.oriel.core.Sharing.shareLink(photo)}", "Share this wallpaper")
                    } catch (e: uz.akbarjon.oriel.data.OrielException) {
                        snackbar.showSnackbar(e.message ?: "Couldn't share")
                    }
                }
            })
            if (onDelete != null) {
                RoundIconButton(OrielIcons.Trash, "Delete from history", onDelete, tint = MaterialTheme.colorScheme.error)
            }
        }
    }
}
