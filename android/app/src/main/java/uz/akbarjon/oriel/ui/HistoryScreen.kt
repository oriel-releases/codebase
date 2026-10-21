package uz.akbarjon.oriel.ui

import androidx.compose.ui.draw.clipToBounds
import uz.akbarjon.oriel.ui.Motion
import uz.akbarjon.oriel.ui.Motion.fadeThrough
import uz.akbarjon.oriel.ui.Motion.slideThrough
import androidx.compose.runtime.derivedStateOf
import kotlin.math.roundToInt
import androidx.compose.ui.unit.IntOffset
import androidx.compose.foundation.layout.offset
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyGridState
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import dev.chrisbanes.haze.HazeState
import dev.chrisbanes.haze.HazeStyle
import dev.chrisbanes.haze.HazeTint
import dev.chrisbanes.haze.hazeEffect
import dev.chrisbanes.haze.hazeSource
import kotlinx.coroutines.launch
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.HistoryEntry

@Composable
fun HistoryScreen() {
    val scope = rememberCoroutineScope()
    var favoritesOnly by rememberSaveable { mutableStateOf(false) }
    val version by Oriel.history.version.collectAsState()
    // Both lists, each with its own grid and scroll position: switching shows the other one as it was, at once.
    val all by produceState<List<HistoryEntry>?>(null, version) { value = Oriel.history.list(false) }
    val favorites by produceState<List<HistoryEntry>?>(null, version) { value = Oriel.history.list(true) }
    val allGrid = rememberLazyGridState()
    val favoritesGrid = rememberLazyGridState()
    var selectedId by remember { mutableStateOf<String?>(null) }
    val haze = remember { HazeState() }
    val c = Palette.colors

    // The title scrolls away with the photos; All / Favorites sits under it, then stays at the top once it gets there.
    val titlePx = with(LocalDensity.current) { 72.dp.toPx() }
    val barY by remember {
        derivedStateOf {
            val grid = if (favoritesOnly) favoritesGrid else allGrid
            if (grid.firstVisibleItemIndex == 0) (titlePx - grid.firstVisibleItemScrollOffset).coerceAtLeast(0f) else 0f
        }
    }

    Column(Modifier.fillMaxSize()) {
        Box(Modifier.fillMaxSize()) {
            Box(Modifier.fillMaxSize().hazeSource(haze)) {
                // All or Favorites: the other list just shows, each at its own scroll position.
                val fav = favoritesOnly
                HistoryGrid(if (fav) favorites else all, fav, if (fav) favoritesGrid else allGrid) { selectedId = it }
            }
            // Stays on top while the photos scroll under it, blurred.
            Segmented(
                listOf(false to "All", true to "Favorites"), favoritesOnly, { favoritesOnly = it },
                Modifier.offset { IntOffset(0, barY.roundToInt()) }.padding(horizontal = 24.dp, vertical = 8.dp).fillMaxWidth().clip(CircleShape)
                    .hazeEffect(haze, HazeStyle(backgroundColor = c.paper, tint = HazeTint(c.surface.copy(alpha = 0.55f)), blurRadius = 20.dp, noiseFactor = 0f)),
                track = Color.Transparent,
            )
        }
    }

    // The tiles' picture size (HistoryGrid's), so the photo page starts with the picture the tile already showed.
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    val tileW = with(LocalDensity.current) { 130.dp.roundToPx() }

    // From All (favorites are in it too): un-starring in Favorites keeps the sheet open.
    val selected = all?.firstOrNull { it.photo.id == selectedId } ?: favorites?.firstOrNull { it.photo.id == selectedId }
    if (selected != null) {
        PhotoSheet(
            selected.photo, selected.topic, onDismiss = { selectedId = null },
            heading = friendlyTime(selected.appliedAt),
            tileUrl = selected.photo.thumb(tileW, tileW * screenH / screenW, uz.akbarjon.oriel.core.Positions.of(selected.photo.id)),
            favorite = selected.isFavorite,
            onFavorite = { fav -> uz.akbarjon.oriel.core.FavoritesSync.set(selected.photo.id, fav) },
            onDelete = {
                selectedId = null
                scope.launch { Oriel.history.delete(selected.photo.id) }
            },
        )
    }
}

@Composable
private fun HistoryGrid(entries: List<HistoryEntry>?, favoritesOnly: Boolean, state: LazyGridState, onOpen: (String) -> Unit) {
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    val cellPx = with(LocalDensity.current) { 130.dp.roundToPx() }
    val c = Palette.colors
    LazyVerticalGrid(
        columns = GridCells.Fixed(3), modifier = Modifier.fillMaxSize(), state = state,
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = bottomBarSpace()),
        horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        item(span = { GridItemSpan(3) }, key = "title") {
            // Out to the screen's edge like the other tabs' titles, then room for the tabs floating over the photos.
            Column(Modifier.offset(x = (-16).dp)) {
                ScreenTitle("History")
                Spacer(Modifier.height(56.dp))
            }
        }
        if (entries != null && entries.isEmpty()) {
            item(span = { GridItemSpan(3) }) {
                Column(Modifier.fillMaxWidth().padding(top = 48.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Icon(if (favoritesOnly) OrielIcons.Star else OrielIcons.History, null,
                        tint = c.muted.copy(alpha = 0.5f), modifier = Modifier.size(40.dp))
                    Spacer(Modifier.height(12.dp))
                    Text(if (favoritesOnly) "No favorites yet" else "Nothing yet", style = MaterialTheme.typography.bodyLarge,
                        color = c.muted, textAlign = TextAlign.Center)
                }
            }
        }
        items(entries.orEmpty(), key = { it.photo.id }) { entry ->
            Box(
                Modifier.animateItem().aspectRatio(screenW.toFloat() / screenH).sharedPhoto(entry.photo)
                    .springClick(pressed = 0.94f, ripple = false) { onOpen(entry.photo.id) }.clip(RoundedCornerShape(16.dp)),
            ) {
                RemoteImage(
                    entry.photo.thumb(cellPx, cellPx * screenH / screenW, uz.akbarjon.oriel.core.Positions.of(entry.photo.id)), entry.photo.description, Modifier.fillMaxSize(),
                    placeholder = parseColor(entry.photo.color) ?: c.surface,
                )
                if (entry.isFavorite) {
                    Box(Modifier.align(Alignment.TopEnd).padding(6.dp).size(24.dp).clip(CircleShape)
                        .background(Color.Black.copy(alpha = 0.35f)), contentAlignment = Alignment.Center) {
                        Icon(OrielIcons.StarFilled, "Favorite", tint = StarGold, modifier = Modifier.size(13.dp))
                    }
                }
            }
        }
    }
}
