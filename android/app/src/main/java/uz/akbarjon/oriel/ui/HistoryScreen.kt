package uz.akbarjon.oriel.ui

import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.unit.Dp
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
    // All and Favorites side by side: swiping slides the whole page across, photos and all; the switch follows.
    val pager = androidx.compose.foundation.pager.rememberPagerState { 2 }
    val favoritesOnly = pager.currentPage == 1
    val version by Oriel.history.version.collectAsState()
    // Both lists, each with its own grid and scroll position: switching shows the other one as it was, at once.
    val all by produceState<List<HistoryEntry>?>(null, version) { value = Oriel.history.list(false) }
    val favorites by produceState<List<HistoryEntry>?>(null, version) { value = Oriel.history.list(true) }
    val allGrid = rememberLazyGridState()
    val favoritesGrid = rememberLazyGridState()
    var selectedId by remember { mutableStateOf<String?>(null) }
    // A delete asked for on the photo page, waiting for a yes.
    var deleting by remember { mutableStateOf<HistoryEntry?>(null) }
    val haze = remember { HazeState() }
    val c = Palette.colors

    // One header over both pages (the title and All / Favorites), frosted: the photos scroll on under it, blurred.
    // The title scrolls away with the photos; All / Favorites stays at the top. Mid-swipe it follows both pages' scroll.
    var headerPx by remember { mutableStateOf(0) }
    val headerDp = with(LocalDensity.current) { headerPx.toDp() }
    val titlePx = with(LocalDensity.current) { 72.dp.toPx() }
    val collapse by remember {
        derivedStateOf {
            fun scrolled(g: LazyGridState) = if (g.firstVisibleItemIndex == 0) g.firstVisibleItemScrollOffset.toFloat().coerceAtMost(titlePx) else titlePx
            val f = (pager.currentPage + pager.currentPageOffsetFraction).coerceIn(0f, 1f)
            scrolled(allGrid) * (1 - f) + scrolled(favoritesGrid) * f
        }
    }

    Column(Modifier.fillMaxSize()) {
        // Pulled down: favorites starred or un-starred on the account's other devices come in now.
        PullRefresh({ uz.akbarjon.oriel.core.FavoritesSync.pull() }, Modifier.fillMaxSize()) {
        Box(Modifier.fillMaxSize().clipToBounds()) {
            androidx.compose.foundation.pager.HorizontalPager(pager, Modifier.fillMaxSize().hazeSource(haze), beyondViewportPageCount = 1) { page ->
                // Each list keeps its own scroll position. Only the page in view lets its tiles fly into the photo page:
                // the same photo is on both pages, and two tiles with one photo would be taken for each other.
                val fav = page == 1
                HistoryGrid(if (fav) favorites else all, fav, if (fav) favoritesGrid else allGrid, headerDp,
                    shared = page == pager.currentPage) { selectedId = it }
            }
            Column(
                Modifier.fillMaxWidth().offset { IntOffset(0, -collapse.roundToInt()) }.onSizeChanged { headerPx = it.height },
            ) {
                ScreenTitle("History")
                // Tapping slides the pages too; swiping them moves the switch.
                Segmented(
                    listOf(false to "All", true to "Favorites"), favoritesOnly,
                    { fav -> scope.launch { pager.animateScrollToPage(if (fav) 1 else 0) } },
                    // Only the switch is frosted: the photos scroll on under it, blurred; the line around it stays clear.
                    Modifier.padding(horizontal = 24.dp, vertical = 8.dp).fillMaxWidth().clip(CircleShape)
                        .hazeEffect(haze, HazeStyle(backgroundColor = c.paper, tint = HazeTint(c.surface.copy(alpha = 0.55f)), blurRadius = 20.dp, noiseFactor = 0f)),
                    track = Color.Transparent,
                    // The pill rides along with the pages, under the finger.
                    position = { (pager.currentPage + pager.currentPageOffsetFraction).coerceIn(0f, 1f) },
                )
            }
        }
        }
    }

    // The tiles' picture size (HistoryGrid's), so the photo page starts with the picture the tile already showed.
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    val tileW = with(LocalDensity.current) { 130.dp.roundToPx() }

    // From All (favorites are in it too): un-starring in Favorites keeps the sheet open.
    val selected = all?.firstOrNull { it.photo.id == selectedId } ?: favorites?.firstOrNull { it.photo.id == selectedId }
    if (selected != null) {
        // Swiping on the photo page goes through the list it was opened from.
        val list = (if (favoritesOnly) favorites else all).orEmpty()
        val at = list.indexOfFirst { it.photo.id == selected.photo.id }
        PhotoSheet(
            selected.photo, selected.topic, onDismiss = { selectedId = null },
            heading = friendlyTime(selected.appliedAt),
            tileUrl = selected.photo.thumb(tileW, tileW * screenH / screenW, uz.akbarjon.oriel.core.Positions.of(selected.photo.id)),
            favorite = selected.isFavorite,
            onFavorite = { fav -> uz.akbarjon.oriel.core.FavoritesSync.set(selected.photo.id, fav) },
            onDelete = { deleting = selected },
            peek = { dir -> if (at >= 0) list.getOrNull(at + dir)?.photo else null },
            onStep = { dir -> selectedId = list[at + dir].photo.id },
            favoriteOf = { id -> list.firstOrNull { it.photo.id == id }?.isFavorite },
        )
    }

    deleting?.let { entry ->
        val close = { deleting = null }
        OrielSheet(close, "Delete this wallpaper?") {
            Text(
                if (entry.isFavorite) "It's a favorite. It leaves your History and your favorites on this phone."
                else "It leaves your History on this phone.",
                style = MaterialTheme.typography.bodyLarge, color = Palette.colors.muted, modifier = Modifier.padding(horizontal = 24.dp),
            )
            SheetActions("Delete", {
                deleting = null
                selectedId = null
                scope.launch { Oriel.history.delete(entry.photo.id) }
            }, "Cancel", close, destructive = true)
        }
    }
}

@Composable
private fun HistoryGrid(
    entries: List<HistoryEntry>?, favoritesOnly: Boolean, state: LazyGridState, top: Dp, shared: Boolean, onOpen: (String) -> Unit,
) {
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    val cellPx = with(LocalDensity.current) { 130.dp.roundToPx() }
    val c = Palette.colors
    LazyVerticalGrid(
        // Three upright; on its side as many as fit.
        columns = GridCells.Adaptive(104.dp), modifier = Modifier.fillMaxSize(), state = state,
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = top + 4.dp, bottom = bottomBarSpace()),
        horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        if (entries != null && entries.isEmpty()) {
            item(span = { GridItemSpan(maxLineSpan) }) {
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
                Modifier.animateItem().aspectRatio(screenW.toFloat() / screenH).let { if (shared) it.sharedPhoto(entry.photo) else it }
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
