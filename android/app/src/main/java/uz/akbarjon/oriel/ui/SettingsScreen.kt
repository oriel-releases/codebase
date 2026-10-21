package uz.akbarjon.oriel.ui

import androidx.compose.ui.draw.clipToBounds
import uz.akbarjon.oriel.ui.Motion
import uz.akbarjon.oriel.ui.Motion.fadeThrough
import uz.akbarjon.oriel.ui.Motion.slideThrough
import androidx.compose.material3.Icon
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.ui.draw.shadow
import kotlin.math.roundToInt
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TimePicker
import androidx.compose.material3.TimePickerDefaults
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.ZoneId
import uz.akbarjon.oriel.BuildConfig
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.Schedule
import uz.akbarjon.oriel.core.Scheduler
import uz.akbarjon.oriel.core.UpdateState
import uz.akbarjon.oriel.core.Updater
import uz.akbarjon.oriel.data.OrielException
import uz.akbarjon.oriel.data.Screen
import uz.akbarjon.oriel.data.Settings
import uz.akbarjon.oriel.data.Source

private enum class Sheet { Interval, Time, Zone, Screen, Position, SignOut }

internal val INTERVALS = listOf(0 to "Once a day", 60 to "Every hour", 180 to "Every 3 hours", 360 to "Every 6 hours", 720 to "Every 12 hours")

@Composable
fun SettingsScreen(onSignedOut: () -> Unit) {
    // Friends lives here (not in the tab bar): set up once, opened now and then. It slides in like Interests in Explore.
    var friends by androidx.compose.runtime.saveable.rememberSaveable { mutableStateOf(false) }
    androidx.compose.animation.AnimatedContent(friends, transitionSpec = { slideThrough(forward = targetState) }, label = "friends") { open ->
        Box(Modifier.fillMaxSize().clipToBounds()) {
            if (open) FriendsScreen(onBack = { friends = false }) else SettingsPage(onSignedOut, onFriends = { friends = true })
        }
    }
}

@Composable
private fun SettingsPage(onSignedOut: () -> Unit, onFriends: () -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val settings by Oriel.store.settings.collectAsState()
    val account by Oriel.auth.account.collectAsState()
    var sheet by remember { mutableStateOf<Sheet?>(null) }
    var signingOut by remember { mutableStateOf(false) }

    /** Saves a schedule change and moves the alarm to match. The wallpaper itself waits for the next tick. */
    fun change(update: (Settings) -> Settings) {
        Oriel.store.update(update)
        Scheduler.restart(context)
    }


    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        ScreenTitle("Settings")

        // The same sections as the computers: Account, Schedule and Photos (every device), Display (this phone), About.
        SectionLabel("Account")
        Group {
            account?.let { a ->
                SettingRow(
                    a.displayName.ifBlank { a.email.substringBefore('@') }, a.email.takeIf { a.displayName.isNotBlank() },
                    leading = { ProfilePicture(a.photoUrl, a.displayName.ifBlank { a.email }, 40.dp) },
                )
                RowDivider()
            }
            SettingRow("Devices", icon = OrielIcons.Monitor, onClick = { openUrl(context, "https://oriel.akbarjon.uz/me") })
            RowDivider()
            SettingRow("Sign out", icon = OrielIcons.LogOut, titleColor = MaterialTheme.colorScheme.error,
                onClick = { sheet = Sheet.SignOut })
        }

        Spacer(Modifier.height(12.dp))
        Group {
            SettingRow("Friends", settings.followName?.takeIf { settings.isFollowing }?.let { "Following $it" }, OrielIcons.Users,
                onClick = onFriends)
        }

        androidx.compose.animation.AnimatedVisibility(
            !settings.isFollowing, enter = Reveal.enter, exit = Reveal.exit,
        ) { Column {
            SectionLabel("Schedule")
            Group {
                SwitchRow("Change automatically", OrielIcons.Refresh, checked = !settings.paused) { on -> change { it.copy(paused = !on) } }
                RowDivider()
                SettingRow("How often", INTERVALS.firstOrNull { it.first == settings.intervalMinutes }?.second ?: "Every ${settings.intervalMinutes} min",
                    OrielIcons.Calendar, onClick = { sheet = Sheet.Interval })
                RowDivider()
                SettingRow(if (settings.intervalMinutes == 0) "Time" else "Starting at", settings.dailyTime, OrielIcons.Clock,
                    onClick = { sheet = Sheet.Time })
                RowDivider()
                SettingRow("Time zone", settings.timeZone.substringAfterLast('/').replace('_', ' '), OrielIcons.Globe,
                    onClick = { sheet = Sheet.Zone })
            }

            SectionLabel("Photos")
            Group {
                SwitchRow("Favorites only", OrielIcons.Star, checked = settings.favoritesOnly) { on ->
                    Oriel.store.update { it.copy(favoritesOnly = on) }
                }
                RowDivider()
                SwitchRow("Match the time of day", OrielIcons.Clock, checked = settings.matchTimeOfDay) { on ->
                    Oriel.store.update { it.copy(matchTimeOfDay = on) }
                }
                RowDivider()
                SwitchRow("Popular photos only", OrielIcons.Trending, checked = settings.skipLowLikes) { on ->
                    Oriel.store.update { it.copy(skipLowLikes = on) }
                }
                // The slider unfolds under its switch, and folds away again.
                androidx.compose.animation.AnimatedVisibility(settings.skipLowLikes, enter = Reveal.enter, exit = Reveal.exit) {
                    Column {
                        RowDivider()
                        MinLikesSlider(settings.minLikes) { picked -> Oriel.store.update { it.copy(minLikes = picked) } }
                    }
                }
            }
        } }

        // Only this phone's: how wallpapers sit on its screen. Never synced (other devices have other screens).
        SectionLabel("Display")
        Group {
            SettingRow("Set on", when (settings.screen) {
                Screen.Home -> "Home"; Screen.Lock -> "Lock"; Screen.Both -> "Both"
            }, OrielIcons.Phone, onClick = { sheet = Sheet.Screen })
            RowDivider()
            SettingRow("Photo position", if (settings.photoPosition == "center") "Center" else "Smart", OrielIcons.Crop,
                onClick = { sheet = Sheet.Position })
        }
        Text("Adjust any wallpaper on Home. These stay on this phone.", style = MaterialTheme.typography.bodySmall,
            color = Palette.colors.muted, modifier = Modifier.padding(start = 32.dp, end = 32.dp, top = 8.dp))

        SectionLabel("About")
        Group {
            val update by Updater.state.collectAsState()
            SettingRow(
                "Version",
                when (val u = update) {
                    is UpdateState.Checking -> "Checking…"
                    is UpdateState.Downloading -> "Downloading ${u.version}…"
                    is UpdateState.Installing -> "Installing ${u.version}…"
                    is UpdateState.NeedsConfirm -> "Install ${u.version}"
                    is UpdateState.UpToDate -> "${BuildConfig.VERSION_NAME} · Up to date"
                    else -> "${BuildConfig.VERSION_NAME} beta"
                },
                OrielIcons.Info,
                onClick = if (Updater.enabled) ({ if (!Updater.confirm(context)) Updater.checkSoon(context, force = true) }) else null,
            )
            RowDivider()
            SettingRow("Photos by Unsplash", icon = OrielIcons.Image,
                onClick = { openUrl(context, "https://unsplash.com/?utm_source=oriel&utm_medium=referral") })
            RowDivider()
            // The AI wallpapers come from Pollinations: credited like Unsplash.
            SettingRow("Pictures made with Pollinations", icon = OrielIcons.Sparkles, onClick = { openUrl(context, "https://pollinations.ai/") })
            RowDivider()
            SettingRow("Get help", icon = OrielIcons.Send, onClick = { openUrl(context, "https://t.me/oriel_support_bot") })
            RowDivider()
            SettingRow("Privacy", icon = OrielIcons.Shield, onClick = { openUrl(context, "https://oriel.akbarjon.uz/privacy") })
        }
        BottomBarSpacer()
    }

    val close = { sheet = null }
    when (sheet) {
        Sheet.Position -> ChoiceSheet("Photo position",
            listOf("smart" to "Smart: faces and detail stay in", "center" to "Center: the middle of the photo"),
            settings.photoPosition, close, icons = listOf(OrielIcons.Star, OrielIcons.Move)) { picked ->
            Oriel.store.update { it.copy(photoPosition = picked) }
            uz.akbarjon.oriel.core.Positions.touch() // previews crop again
            sheet = null
        }
        Sheet.Interval -> ChoiceSheet("How often", INTERVALS, settings.intervalMinutes, close) { picked ->
            change { it.copy(intervalMinutes = picked) }; sheet = null
        }
        Sheet.Screen -> ChoiceSheet("Set on", listOf(Screen.Both to "Home and lock screen", Screen.Home to "Home screen", Screen.Lock to "Lock screen"),
            settings.screen, close) { picked -> Oriel.store.update { it.copy(screen = picked) }; sheet = null }
        Sheet.Time -> TimeSheet(Schedule.dailyTime(settings), close) { hour, minute ->
            change { it.copy(dailyTime = "%02d:%02d".format(hour, minute)) }; sheet = null
        }
        Sheet.Zone -> ZoneSheet(settings.timeZone, close) { zone -> change { it.copy(timeZone = zone) }; sheet = null }
        Sheet.SignOut -> OrielSheet(close, "Sign out?") {
            Text("Oriel stops changing your wallpaper on this phone until you sign in again.",
                style = MaterialTheme.typography.bodyLarge, color = Palette.colors.muted, modifier = Modifier.padding(horizontal = 24.dp))
            SheetActions("Sign out", {
                sheet = null
                signingOut = true
                Oriel.scope.launch {
                    Oriel.auth.signOut()
                    Scheduler.cancelAll(context)
                    Oriel.history.clear()
                    Oriel.store.reset()
                    withContext(Dispatchers.Main) {
                        signingOut = false
                        onSignedOut()
                    }
                }
            }, "Cancel", close, destructive = true)
        }
        null -> Unit
    }

    // Signing out: nothing can be tapped until it's done.
    if (signingOut) {
        OrielSheet({}, dismissible = false) {
            Column(Modifier.fillMaxWidth().padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                CircularProgressIndicator(Modifier.size(28.dp), strokeWidth = 2.5.dp)
                Spacer(Modifier.height(16.dp))
                Text("Signing out…", style = MaterialTheme.typography.bodyLarge, color = Palette.colors.muted)
            }
        }
    }
}

/** The account's initial in an ink circle. */
@Composable
private fun Avatar(name: String) {
    Box(Modifier.size(40.dp).clip(CircleShape).background(Palette.colors.ink), contentAlignment = Alignment.Center) {
        Text(name.take(1).uppercase(), style = MaterialTheme.typography.titleMedium, color = Palette.colors.paper)
    }
}

@Composable
private fun <T> ChoiceSheet(
    title: String, options: List<Pair<T, String>>, selected: T, onDismiss: () -> Unit,
    icons: List<androidx.compose.ui.graphics.vector.ImageVector?> = options.map { null }, onPick: (T) -> Unit,
) {
    OrielSheet(onDismiss, title) {
        options.forEachIndexed { i, (value, label) -> SheetOption(label, value == selected, { onPick(value) }, icons[i]) }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun TimeSheet(initial: java.time.LocalTime, onDismiss: () -> Unit, onPick: (Int, Int) -> Unit) {
    val context = LocalContext.current
    val state = rememberTimePickerState(initial.hour, initial.minute, android.text.format.DateFormat.is24HourFormat(context))
    val c = Palette.colors
    OrielSheet(onDismiss, "Time") {
        // The hour and minute boxes and the AM/PM switch take the theme's small corners: rounder here, like the rest of the app.
        val round = androidx.compose.foundation.shape.RoundedCornerShape(24.dp)
        Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            MaterialTheme(shapes = MaterialTheme.shapes.copy(extraSmall = round, small = round)) {
            TimePicker(state, colors = TimePickerDefaults.colors(
                clockDialColor = c.surface, clockDialSelectedContentColor = c.paper, clockDialUnselectedContentColor = c.ink,
                selectorColor = c.ink, containerColor = c.paper,
                timeSelectorSelectedContainerColor = c.ink, timeSelectorSelectedContentColor = c.paper,
                timeSelectorUnselectedContainerColor = c.surface, timeSelectorUnselectedContentColor = c.ink,
                periodSelectorBorderColor = c.line,
                periodSelectorSelectedContainerColor = c.ink, periodSelectorSelectedContentColor = c.paper,
                periodSelectorUnselectedContainerColor = c.surface, periodSelectorUnselectedContentColor = c.ink,
            ))
            }
        }
        SheetActions("Save", { onPick(state.hour, state.minute) }, "Cancel", onDismiss)
    }
}

@Composable
private fun ZoneSheet(current: String, onDismiss: () -> Unit, onPick: (String) -> Unit) {
    var filter by remember { mutableStateOf("") }
    val zones = remember { ZoneId.getAvailableZoneIds().filter { '/' in it && !it.startsWith("Etc/") && !it.startsWith("SystemV/") }.sorted() }
    val shown = remember(filter) { zones.filter { filter.isBlank() || it.contains(filter.trim().replace(' ', '_'), ignoreCase = true) } }
    OrielSheet(onDismiss, "Time zone") {
        OrielTextField(filter, { filter = it }, Modifier.padding(horizontal = 16.dp), placeholder = "Search, like Tashkent",
            leadingIcon = OrielIcons.Search)
        Spacer(Modifier.height(8.dp))
        SheetOption("Use this phone's (${ZoneId.systemDefault().id.substringAfterLast('/').replace('_', ' ')})", false,
            { onPick(ZoneId.systemDefault().id) }, OrielIcons.Globe)
        LazyColumn(Modifier.heightIn(max = 380.dp)) {
            items(shown, key = { it }) { zone -> SheetOption(zone.replace('_', ' '), zone == current, { onPick(zone) }) }
        }
    }
}

/**
 * Minimum likes, right in its row: Material's own slider, 300 to 1,000+ in steps of 50, like the computers' Settings.
 * Saved when the finger lifts, so dragging doesn't write the settings on every step.
 */
@Composable
private fun MinLikesSlider(likes: Int, onPick: (Int) -> Unit) {
    val c = Palette.colors
    var value by remember(likes) { mutableFloatStateOf((likes / 50 * 50).coerceIn(300, 1000).toFloat()) }
    Column(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 14.dp, bottom = 4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(OrielIcons.Star, null, tint = c.ink, modifier = Modifier.size(21.dp))
            Spacer(Modifier.width(16.dp))
            Text("Minimum likes", style = MaterialTheme.typography.bodyLarge, color = c.ink, modifier = Modifier.weight(1f))
            Text(if (value >= 1000f) "1,000+ likes" else "%,d likes".format(value.toInt()),
                style = MaterialTheme.typography.bodyMedium, color = c.muted)
        }
        androidx.compose.material3.Slider(
            value = value,
            onValueChange = { value = (it / 50f).roundToInt() * 50f },
            onValueChangeFinished = { onPick(value.toInt()) },
            valueRange = 300f..1000f,
            steps = 13,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

/**
 * How a settings row (or section) comes and goes: it unfolds from the top with a soft spring and fades in a beat after,
 * so the rows under it glide down instead of jumping; leaving, it fades first, then folds away.
 */
private object Reveal {
    val enter = androidx.compose.animation.expandVertically(
        androidx.compose.animation.core.spring(dampingRatio = 0.9f, stiffness = 420f), expandFrom = Alignment.Top,
    ) + androidx.compose.animation.fadeIn(androidx.compose.animation.core.tween(220, delayMillis = 80)) +
        androidx.compose.animation.slideInVertically(androidx.compose.animation.core.spring(dampingRatio = 0.9f, stiffness = 420f)) { -it / 6 }
    val exit = androidx.compose.animation.fadeOut(androidx.compose.animation.core.tween(120)) +
        androidx.compose.animation.shrinkVertically(
            androidx.compose.animation.core.tween(240, delayMillis = 60, easing = androidx.compose.animation.core.FastOutSlowInEasing),
            shrinkTowards = Alignment.Top,
        )
}
