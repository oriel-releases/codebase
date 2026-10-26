package uz.akbarjon.oriel.ui

import androidx.compose.foundation.clickable
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.background
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.data.Interest
import uz.akbarjon.oriel.data.Settings

/** Adds a search to the interests (or turns it back on): the only way to add your own, like the desktop apps. */
fun addInterest(words: String) {
    val query = words.trim().lowercase().take(40)
    if (query.isEmpty()) return
    Oriel.store.update { s ->
        if (s.interests.any { it.query.equals(query, ignoreCase = true) })
            s.copy(interests = s.interests.map { if (it.query.equals(query, ignoreCase = true)) it.copy(enabled = true) else it })
        else s.copy(interests = s.interests + Interest(query, enabled = true, isCustom = true))
    }
}

/** True when this search is already one of the enabled interests. */
fun isInterest(words: String): Boolean =
    Oriel.store.settings.value.interests.any { it.enabled && it.query.equals(words.trim(), ignoreCase = true) }

/**
 * The topics Oriel picks from (the desktop's former "Daily Mix"): built-in chips you turn on and off, and your own words,
 * which come from Explore's search ("Add to Interests"), like the desktop apps. Opened from Explore; [onBack] returns there.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun InterestsScreen(onBack: () -> Unit) {
    val settings by Oriel.store.settings.collectAsState()
    val c = Palette.colors
    BackHandler(onBack = onBack)

    fun toggle(i: Interest) = Oriel.store.update { s ->
        s.copy(interests = s.interests.map { if (it.query == i.query) it.copy(enabled = !it.enabled) else it })
    }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).readableWidth()) {
        Row(Modifier.fillMaxWidth().height(72.dp).padding(start = 8.dp, end = 24.dp, top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onBack) { Icon(OrielIcons.ChevronRight, "Back", Modifier.size(24.dp).graphicsLayer { rotationZ = 180f }) }
            Text("Interests", style = androidx.compose.material3.MaterialTheme.typography.headlineMedium, modifier = Modifier.weight(1f))
            Text("${settings.interests.count { it.enabled }} on", style = androidx.compose.material3.MaterialTheme.typography.labelMedium, color = c.muted)
        }

        Spacer(Modifier.height(20.dp))
        FlowRow(Modifier.padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)) {
            settings.interests.filter { !it.isCustom }.forEach { interest ->
                ToggleChip(interest.query.replaceFirstChar { it.uppercase() }, interest.enabled, { toggle(interest) },
                    leading = Settings.EMOJI[interest.query] ?: "✨")
            }
        }

        SectionLabel("Your own")
        val custom = settings.interests.filter { it.isCustom }
        if (custom.isEmpty()) {
            Text("Search for anything, then tap Add to Interests to make it one of yours.",
                style = androidx.compose.material3.MaterialTheme.typography.bodyMedium, color = c.muted,
                modifier = Modifier.padding(horizontal = 32.dp))
        } else {
            Group {
                custom.forEachIndexed { i, interest ->
                    if (i > 0) RowDivider()
                    SettingRow(interest.query.replaceFirstChar { it.uppercase() }, icon = OrielIcons.Search,
                        onClick = { toggle(interest) }) {
                        Box(
                            Modifier.size(32.dp).clip(CircleShape)
                                .clickable { Oriel.store.update { s -> s.copy(interests = s.interests.filter { it.query != interest.query }) } },
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(OrielIcons.Close, "Remove ${interest.query}", tint = c.muted, modifier = Modifier.size(18.dp))
                        }
                        Spacer(Modifier.width(8.dp))
                        OrielSwitch(interest.enabled) { toggle(interest) }
                    }
                }
            }
        }
        BottomBarSpacer()
    }
}
