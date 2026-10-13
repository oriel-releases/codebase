package uz.akbarjon.oriel.ui

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
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
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
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.OrielException
import uz.akbarjon.oriel.data.Photo
import uz.akbarjon.oriel.data.Unsplash

/** Kept for the life of the app, so coming back to Search doesn't spend Unsplash requests again. */
private object SearchModel {
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

private val SUGGESTIONS = listOf("northern lights", "tokyo night", "foggy forest", "desert dunes", "city skyline",
    "snowy mountains", "calm ocean", "milky way")

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
            Modifier.size(64.dp).clip(androidx.compose.foundation.shape.RoundedCornerShape(16.dp)))
        androidx.compose.foundation.layout.Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text("Wallpaper of the day", style = MaterialTheme.typography.bodyLarge)
            Text("by ${photo.photographer}", style = MaterialTheme.typography.bodySmall, color = c.muted, maxLines = 1)
        }
        Icon(OrielIcons.ChevronRight, null, tint = c.muted.copy(alpha = 0.6f), modifier = Modifier.size(18.dp))
    }
}

/** Search, with Interests inside it (like the desktop apps): a row that opens them, and "Add to Interests" for a search. */
@Composable
fun SearchScreen() {
    var interests by rememberSaveable { mutableStateOf(false) }
    AnimatedContent(interests, transitionSpec = {
        if (targetState) (slideInHorizontally { it / 3 } + fadeIn()) togetherWith (slideOutHorizontally { -it / 3 } + fadeOut())
        else (slideInHorizontally { -it / 3 } + fadeIn()) togetherWith (slideOutHorizontally { it / 3 } + fadeOut())
    }, label = "interests") { open ->
        if (open) InterestsScreen(onBack = { interests = false }) else SearchPage(onInterests = { interests = true })
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun SearchPage(onInterests: () -> Unit) {
    val settings by Oriel.store.settings.collectAsState()
    val scope = rememberCoroutineScope()
    val focus = LocalFocusManager.current
    val m = SearchModel
    var selected by remember { mutableStateOf<Photo?>(null) }
    val grid = rememberLazyGridState()
    val (screenW, screenH) = remember { Oriel.wallpapers.screenSize() }
    // Two columns: each thumbnail is fetched at half the screen width, already cropped to the phone's shape.
    val cellPx = with(LocalDensity.current) { 180.dp.roundToPx() }
    val c = Palette.colors

    val nearEnd by remember { derivedStateOf { grid.layoutInfo.visibleItemsInfo.lastOrNull()?.index?.let { it >= grid.layoutInfo.totalItemsCount - 6 } == true } }
    LaunchedEffect(nearEnd, m.photos.size) { if (nearEnd) m.more(scope) }

    Column(Modifier.fillMaxSize()) {
        ScreenTitle("Search")
        OrielTextField(
            m.text, { m.text = it }, Modifier.padding(horizontal = 16.dp), placeholder = "Northern lights, tokyo night…",
            leadingIcon = OrielIcons.Search,
            trailing = if (m.text.isNotEmpty()) ({ IconButton({ m.clear() }) { Icon(OrielIcons.Close, "Clear", tint = c.muted) } }) else null,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { focus.clearFocus(); m.search(scope, m.text) }),
        )

        LazyVerticalGrid(
            columns = GridCells.Fixed(2), state = grid, modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 16.dp, bottom = bottomBarSpace()),
            horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            if (m.query.isEmpty()) {
                item(span = { GridItemSpan(2) }) {
                    Box(Modifier.padding(bottom = 8.dp).clip(androidx.compose.foundation.shape.RoundedCornerShape(22.dp)).background(c.surface)) {
                        SettingRow("Interests", "${settings.interests.count { it.enabled }} on", OrielIcons.Heart, onClick = onInterests)
                    }
                }
                item(span = { GridItemSpan(2) }) { TodayCard { selected = it } }
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
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        SUGGESTIONS.forEach { s -> ToggleChip(s.replaceFirstChar { it.uppercase() }, false, { focus.clearFocus(); m.search(scope, s) }) }
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
            items(m.photos, key = { it.id }) { photo ->
                RemoteImage(
                    photo.thumb(cellPx, cellPx * screenH / screenW), photo.description,
                    Modifier.animateItem().aspectRatio(screenW.toFloat() / screenH)
                        .springClick(pressed = 0.95f, ripple = false) { selected = photo }.clip(RoundedCornerShape(20.dp)),
                    placeholder = parseColor(photo.color) ?: c.surface,
                )
            }
            if (m.loading) {
                item(span = { GridItemSpan(2) }) {
                    Box(Modifier.fillMaxWidth().padding(16.dp), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator(Modifier.size(24.dp), strokeWidth = 2.dp)
                    }
                }
            }
        }
    }

    selected?.let { photo -> PhotoSheet(photo, topic = "search", onDismiss = { selected = null }) }
}
