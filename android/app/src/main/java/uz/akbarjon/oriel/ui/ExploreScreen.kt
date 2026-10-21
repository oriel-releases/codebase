package uz.akbarjon.oriel.ui

import androidx.compose.ui.draw.clipToBounds
import uz.akbarjon.oriel.ui.Motion
import uz.akbarjon.oriel.ui.Motion.fadeThrough
import uz.akbarjon.oriel.ui.Motion.slideThrough
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.produceState
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Row
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SuggestionChip
import androidx.compose.material3.SuggestionChipDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.ui.graphics.Color
import dev.chrisbanes.haze.HazeState
import dev.chrisbanes.haze.HazeStyle
import dev.chrisbanes.haze.HazeTint
import dev.chrisbanes.haze.hazeEffect
import dev.chrisbanes.haze.hazeSource
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.ui.unit.IntOffset
import kotlin.math.roundToInt
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.Explore
import uz.akbarjon.oriel.core.ExploreFeed
import uz.akbarjon.oriel.data.OrielException
import uz.akbarjon.oriel.data.Photo
import uz.akbarjon.oriel.data.Unsplash

/** Kept for the life of the app, so coming back to Explore doesn't spend Unsplash requests again. */
private object SearchModel {
    /** This hour's photos, the same for everyone (no Unsplash requests to scroll them). */
    var explore by mutableStateOf(ExploreFeed.Empty)
    var exploreLoading by mutableStateOf(false)

    fun loadExplore(scope: CoroutineScope) {
        if (exploreLoading) return
        exploreLoading = true
        scope.launch {
            try {
                explore = Explore.feed()
            } finally {
                exploreLoading = false
            }
        }
    }

    var text by mutableStateOf("")
    var query by mutableStateOf("")
    var photos by mutableStateOf<List<Photo>>(emptyList())
    var page by mutableStateOf(0)
    var totalPages by mutableStateOf(0)
    var loading by mutableStateOf(false)
    var error by mutableStateOf<String?>(null)

    /** Searching stops a little before the hourly limit so the scheduled change still gets its photo. */
    private const val RESERVE = 10

    fun search(scope: CoroutineScope, words: String) {
        val q = words.trim()
        if (q.isEmpty()) return
        text = q; query = q; photos = emptyList(); page = 0; totalPages = 0; error = null
        more(scope)
    }

    fun more(scope: CoroutineScope) {
        if (loading || query.isEmpty() || (page > 0 && page >= totalPages)) return
        if ((Unsplash.remaining ?: Int.MAX_VALUE) < RESERVE) {
            error = "Searching is paused for a while so your scheduled wallpaper still gets its photos. Try again in the next hour."
            return
        }
        loading = true
        val forQuery = query
        scope.launch {
            try {
                val result = Unsplash.search(forQuery, page + 1)
                if (forQuery != query) return@launch
                // Free photos only, big enough for this screen.
                val (w, h) = Oriel.wallpapers.screenSize()
                val fits = result.photos.filter { it.rawUrl.startsWith("https://images.unsplash.com/") && it.width >= w && it.height >= h }
                photos = (photos + fits).distinctBy { it.id }
                page += 1
                totalPages = result.totalPages
            } catch (e: OrielException) {
                error = e.message
            } catch (e: Exception) {
                error = "Couldn't search right now. Try again."
            } finally {
                loading = false
            }
        }
    }

    fun clear() {
        text = ""; query = ""; photos = emptyList(); page = 0; totalPages = 0; error = null
    }
}

/** Wallpaper of the day: the same photo as oriel.akbarjon.uz/today and the desktop apps' Home. Tap to see and set it. */
@Composable
private fun TodayCard(onOpen: (Photo) -> Unit) {
    val today by produceState<Photo?>(null) { value = uz.akbarjon.oriel.core.Today.photo() }
    val photo = today ?: return
    val c = Palette.colors
    val thumbPx = with(LocalDensity.current) { 64.dp.roundToPx() }
    Row(
        Modifier.fillMaxWidth().padding(bottom = 8.dp).clip(androidx.compose.foundation.shape.RoundedCornerShape(22.dp)).background(c.surface)
            .springClick(pressed = 0.98f) { onOpen(photo) }.padding(10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        RemoteImage(photo.thumb(thumbPx, thumbPx), photo.description,
            Modifier.size(64.dp).sharedPhoto(photo).clip(androidx.compose.foundation.shape.RoundedCornerShape(16.dp)))
        androidx.compose.foundation.layout.Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text("Wallpaper of the day", style = MaterialTheme.typography.bodyLarge)
            Text("by ${photo.photographer}", style = MaterialTheme.typography.bodySmall, color = c.muted, maxLines = 1)
        }
        Icon(OrielIcons.ChevronRight, null, tint = c.muted.copy(alpha = 0.6f), modifier = Modifier.size(18.dp))
    }
}

/**
 * Explore: search at the top, then this hour's wallpapers (the same for everyone), with Interests inside it like the
 * desktop apps: a row that opens them, and "Add to Interests" for a search.
 */
@Composable
fun ExploreScreen() {
    var interests by rememberSaveable { mutableStateOf(false) }
    AnimatedContent(interests, transitionSpec = { slideThrough(forward = targetState) }, label = "interests") { open ->
        Box(Modifier.fillMaxSize().clipToBounds()) {
            if (open) InterestsScreen(onBack = { interests = false }) else ExplorePage(onInterests = { interests = true })
        }
    }
}

@Composable
private fun ExplorePage(onInterests: () -> Unit) {
    val settings by Oriel.store.settings.collectAsState()
    val scope = rememberCoroutineScope()
    val focus = LocalFocusManager.current
    val m = SearchModel
    var selected by remember { mutableStateOf<Photo?>(null) }
    // What History keeps as its interest: the one an Explore photo was found under, or "search".
    var selectedTopic by remember { mutableStateOf("search") }
    // The picture the tapped tile showed: the photo page starts with it (already loaded).
    var selectedTile by remember { mutableStateOf<String?>(null) }
    var selectedHeading by remember { mutableStateOf<String?>(null) }
    // Every visit: this hour's photos, when the hour has turned since the last ones.
    LaunchedEffect(Unit) { m.loadExplore(scope) }
    val exploring = m.query.isEmpty()
    val shown = if (exploring) m.explore.photos else m.photos
    val grid = rememberLazyGridState()
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    // Two columns: each thumbnail is fetched at half the screen width, already cropped to the phone's shape.
    val cellPx = with(LocalDensity.current) { 180.dp.roundToPx() }
    val c = Palette.colors
    val searchHaze = remember { HazeState() }

    val nearEnd by remember { derivedStateOf { grid.layoutInfo.visibleItemsInfo.lastOrNull()?.index?.let { it >= grid.layoutInfo.totalItemsCount - 6 } == true } }
    LaunchedEffect(nearEnd, m.photos.size) { if (nearEnd) m.more(scope) }

    // The title scrolls away with the photos; the search bar sits under it, then stays at the top once it gets there.
    val titlePx = with(LocalDensity.current) { 72.dp.toPx() }
    val barY by remember {
        derivedStateOf { if (grid.firstVisibleItemIndex == 0) (titlePx - grid.firstVisibleItemScrollOffset).coerceAtLeast(0f) else 0f }
    }

    Column(Modifier.fillMaxSize()) {
        // The search bar floats over the photos: they scroll on under it, blurred, like under the navigation bar.
        Box(Modifier.weight(1f)) {
        LazyVerticalGrid(
            columns = GridCells.Fixed(2), state = grid, modifier = Modifier.fillMaxSize().hazeSource(searchHaze),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = bottomBarSpace()),
            horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            item(span = { GridItemSpan(2) }, key = "title") {
                // Out to the screen's edge like the other tabs' titles, then room for the search bar.
                Column(Modifier.offset(x = (-16).dp)) {
                    ScreenTitle("Explore")
                    Spacer(Modifier.height(56.dp + 14.dp))
                }
            }
            if (m.query.isEmpty()) {
                item(span = { GridItemSpan(2) }) {
                    Box(Modifier.padding(bottom = 8.dp).clip(androidx.compose.foundation.shape.RoundedCornerShape(22.dp)).background(c.surface)) {
                        SettingRow("Interests", "${settings.interests.count { it.enabled }} on", OrielIcons.Heart, onClick = onInterests)
                    }
                }
                item(span = { GridItemSpan(2) }) { TodayCard { selectedTopic = "search"; selectedTile = null; selectedHeading = "Wallpaper of the day"; selected = it } }
            }
            if (m.query.isNotEmpty()) {
                item(span = { GridItemSpan(2) }) {
                    // A search worth keeping becomes one of your interests: the schedule then picks from it too.
                    val added = settings.interests.any { it.enabled && it.query.equals(m.query.trim(), ignoreCase = true) }
                    Row(Modifier.fillMaxWidth().padding(bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text("“${m.query}”", style = MaterialTheme.typography.titleMedium, maxLines = 1, modifier = Modifier.weight(1f))
                        ToggleChip(if (added) "In your Interests" else "Add to Interests", added, { if (!added) addInterest(m.query) })
                    }
                }
            }
            if (m.query.isEmpty()) {
                item(span = { GridItemSpan(2) }) {
                    Column(Modifier.padding(top = 10.dp, bottom = 2.dp)) {
                        Text("Fresh picks for you", style = MaterialTheme.typography.titleMedium)
                        if (m.explore.photos.isEmpty() && !m.exploreLoading) {
                            Text("Fresh picks couldn't load right now. Check your connection, or search above.",
                                style = MaterialTheme.typography.bodySmall, color = c.muted)
                        }
                    }
                }
            }
            m.error?.let { message ->
                item(span = { GridItemSpan(2) }) {
                    Text(message, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error,
                        modifier = Modifier.padding(vertical = 8.dp))
                }
            }
            if (m.query.isNotEmpty() && !m.loading && m.photos.isEmpty() && m.error == null && m.page > 0) {
                item(span = { GridItemSpan(2) }) {
                    Text("No photos for “${m.query}”", style = MaterialTheme.typography.bodyLarge, color = c.muted,
                        textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 48.dp))
                }
            }
            items(shown, key = { it.id }) { photo ->
                RemoteImage(
                    photo.thumb(cellPx, cellPx * screenH / screenW), photo.description,
                    Modifier.aspectRatio(screenW.toFloat() / screenH).sharedPhoto(photo)
                        .springClick(pressed = 0.95f, ripple = false) {
                            selectedTopic = if (exploring) m.explore.topics[photo.id] ?: "explore" else "search"
                            selectedTile = photo.thumb(cellPx, cellPx * screenH / screenW)
                            selectedHeading = null
                            selected = photo
                        }.clip(RoundedCornerShape(20.dp)),
                    placeholder = parseColor(photo.color) ?: c.surface,
                )
            }
            if (m.loading || (exploring && m.exploreLoading && m.explore.photos.isEmpty())) {
                item(span = { GridItemSpan(2) }) {
                    Box(Modifier.fillMaxWidth().padding(16.dp), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator(Modifier.size(24.dp), strokeWidth = 2.dp)
                    }
                }
            }
        }
        OrielTextField(
            m.text, { m.text = it },
            Modifier.offset { IntOffset(0, barY.roundToInt()) }.padding(horizontal = 24.dp, vertical = 8.dp).clip(CircleShape)
                .hazeEffect(searchHaze, HazeStyle(backgroundColor = c.paper, tint = HazeTint(c.surface.copy(alpha = 0.6f)), blurRadius = 20.dp, noiseFactor = 0f)),
            placeholder = "Northern lights, tokyo night…",
            leadingIcon = OrielIcons.Search,
            trailing = if (m.text.isNotEmpty()) ({ IconButton({ m.clear() }) { Icon(OrielIcons.Close, "Clear", tint = c.muted) } }) else null,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { focus.clearFocus(); m.search(scope, m.text) }),
            background = Color.Transparent,
        )
        }
    }

    selected?.let { photo -> PhotoSheet(photo, topic = selectedTopic, onDismiss = { selected = null }, heading = selectedHeading, tileUrl = selectedTile) }
}
