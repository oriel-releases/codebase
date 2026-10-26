package uz.akbarjon.oriel.ui

import uz.akbarjon.oriel.ui.Motion
import uz.akbarjon.oriel.ui.Motion.fadeThrough
import uz.akbarjon.oriel.ui.Motion.slideThrough
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.Scheduler
import uz.akbarjon.oriel.data.Screen
import uz.akbarjon.oriel.data.Settings

/** The tour's steps: 0 welcome, 1 interests, 2 how often, 3 where it shows, 4 done. */
private const val LAST_STEP = 4

/**
 * The first-run tour, once after the first sign-in on this phone, like the computers': what Oriel does, then a few
 * questions (interests, how often, where it shows) and the first wallpaper. Nothing is saved until the end; skipping
 * keeps every setting as it is and still sets a first wallpaper.
 */
@Composable
fun OnboardingScreen(onDone: () -> Unit) {
    val start = remember { Oriel.store.settings.value }
    var step by rememberSaveable { mutableIntStateOf(0) }
    var picked by rememberSaveable { mutableStateOf(start.interests.filter { !it.isCustom && it.enabled }.map { it.query }) }
    var interval by rememberSaveable { mutableIntStateOf(start.intervalMinutes) }
    var time by rememberSaveable { mutableStateOf(start.dailyTime) }
    var screen by rememberSaveable { mutableStateOf(start.screen) }
    var pickingTime by remember { mutableStateOf(false) }
    val c = Palette.colors

    BackHandler(step in 1..3) { step-- }

    // A first wallpaper either way, unless one came from the account's other devices meanwhile.
    fun firstWallpaper() {
        if (Oriel.store.state.value.lastAppliedAt == 0L) Oriel.scope.launch { Oriel.wallpapers.changeNow() }
    }

    fun skip() {
        Oriel.store.updateState { it.copy(onboardingDone = true) }
        onDone()
        firstWallpaper()
    }

    fun finish() {
        Oriel.store.update { s ->
            s.copy(
                interests = s.interests.map { if (it.isCustom) it else it.copy(enabled = it.query in picked) },
                intervalMinutes = interval, dailyTime = time, screen = screen,
            )
        }
        Scheduler.restart(Oriel.app)
        Oriel.store.updateState { it.copy(onboardingDone = true) }
        onDone()
        firstWallpaper()
    }

    Column(Modifier.fillMaxSize().background(c.paper).systemBarsPadding().padding(horizontal = 24.dp, vertical = 16.dp).readableWidth()) {
        // Progress on the question steps, and Skip until the end.
        Row(Modifier.fillMaxWidth().height(48.dp), verticalAlignment = Alignment.CenterVertically) {
            if (step in 1..3) {
                val progress by animateFloatAsState(step / 3f, tween(300), label = "tourProgress")
                Box(Modifier.weight(1f).height(4.dp).clip(CircleShape).background(c.line)) {
                    Box(Modifier.fillMaxWidth(progress).fillMaxHeight().clip(CircleShape).background(c.ink))
                }
                Spacer(Modifier.width(12.dp))
                Text("$step/3", style = MaterialTheme.typography.labelLarge, color = c.muted)
            } else {
                Spacer(Modifier.weight(1f))
            }
            if (step < LAST_STEP) {
                Spacer(Modifier.width(16.dp))
                Text("Skip", style = MaterialTheme.typography.labelLarge, color = c.muted,
                    modifier = Modifier.clip(CircleShape).clickable { skip() }.padding(horizontal = 12.dp, vertical = 8.dp))
            }
        }

        AnimatedContent(
            step,
            // Swiping goes where Back and Next would (Next waits for an interest on that step); the end is a button.
            Modifier.weight(1f).swipeSteps(
                can = { dir -> if (dir == 1) step in 0..3 && (step != 1 || picked.isNotEmpty()) else step in 1..3 },
            ) { dir -> step += dir },
            transitionSpec = { slideThrough(forward = targetState > initialState) }, label = "tourStep") { s ->
            Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(top = 12.dp, bottom = 16.dp)) {
                when (s) {
                    0 -> Welcome()
                    1 -> Interests(start, picked) { q -> picked = if (q in picked) picked - q else picked + q }
                    2 -> HowOften(interval, time, { interval = it }, { pickingTime = true })
                    3 -> WhereItShows(screen) { screen = it }
                    else -> Done(picked, interval, time, screen)
                }
            }
        }

        // Back and Next; at the end, the first wallpaper.
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            when (step) {
                0 -> PrimaryButton("Get started", { step = 1 }, Modifier.fillMaxWidth())
                LAST_STEP -> PrimaryButton(
                    if (Oriel.store.state.collectAsState().value.lastAppliedAt == 0L) "Set my first wallpaper" else "Start using Oriel",
                    { finish() }, Modifier.fillMaxWidth(),
                )
                else -> {
                    SecondaryButton("Back", { step-- }, Modifier.weight(1f))
                    PrimaryButton("Next", { step++ }, Modifier.weight(1f), enabled = step != 1 || picked.isNotEmpty())
                }
            }
        }
    }

    if (pickingTime) {
        TimeSheet(java.time.LocalTime.parse(time), { pickingTime = false }) { hour, minute ->
            time = "%02d:%02d".format(hour, minute); pickingTime = false
        }
    }
}

@Composable
private fun Heading(title: String, text: String) {
    Text(title, style = MaterialTheme.typography.headlineMedium)
    Spacer(Modifier.height(8.dp))
    Text(text, style = MaterialTheme.typography.bodyLarge, color = Palette.colors.muted)
    Spacer(Modifier.height(24.dp))
}

@Composable
private fun Welcome() {
    Spacer(Modifier.height(24.dp))
    Logo(56.dp)
    Spacer(Modifier.height(24.dp))
    Heading("Welcome to Oriel", "A beautiful new wallpaper, picked for you. Setting up takes under a minute.")
    Feature(OrielIcons.Calendar, "A new photo, on your schedule", "Every day or every few hours, or whenever you tap Change now.")
    Feature(OrielIcons.Heart, "Only what you love", "You pick the interests. Star a favorite to keep it forever.")
    Feature(OrielIcons.Monitor, "The same on every device", "Your phone and computers show the same wallpaper, in seconds.")
    Feature(OrielIcons.Shield, "Quiet and light", "Oriel only wakes up to change your wallpaper, easy on the battery.")
}

@Composable
private fun Feature(icon: ImageVector, title: String, text: String) {
    val c = Palette.colors
    Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.Top) {
        Box(Modifier.size(40.dp).clip(CircleShape).background(c.surface), contentAlignment = Alignment.Center) {
            Icon(icon, null, tint = c.ink, modifier = Modifier.size(20.dp))
        }
        Spacer(Modifier.width(16.dp))
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
            Text(text, style = MaterialTheme.typography.bodyMedium, color = c.muted)
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Interests(start: Settings, picked: List<String>, onToggle: (String) -> Unit) {
    Heading("What do you like?", "Pick a few. Each new wallpaper comes from one of them.")
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        start.interests.filter { !it.isCustom }.forEach { interest ->
            ToggleChip(interest.query.replaceFirstChar { it.uppercase() }, interest.query in picked, { onToggle(interest.query) },
                leading = Settings.EMOJI[interest.query] ?: "✨")
        }
    }
    Spacer(Modifier.height(16.dp))
    val n = picked.size
    Text(
        when (n) { 0 -> "Pick at least one to continue."; 1 -> "1 picked. Pick a few more for variety."; else -> "$n picked. Nice mix." },
        style = MaterialTheme.typography.bodyMedium, color = Palette.colors.muted,
    )
}

/** One line under each choice, so the difference is clear at a glance (the same words as on the computers). */
private fun describe(minutes: Int) = when (minutes) {
    0 -> "A new photo every day, at the time you pick."
    60 -> "Something new all day long."
    180 -> "Morning, afternoon and evening."
    360 -> "A few times a day."
    else -> "Day and night."
}

@Composable
private fun HowOften(interval: Int, time: String, onPick: (Int) -> Unit, onTime: () -> Unit) {
    Heading("How often should it change?", "Most people like one new photo a day. You can change this any time.")
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        INTERVALS.forEach { (minutes, label) ->
            Choice(label, describe(minutes), interval == minutes, { onPick(minutes) }) {
                if (minutes == 0 && interval == 0) {
                    Spacer(Modifier.height(10.dp))
                    Row(
                        Modifier.clip(CircleShape).background(Palette.colors.paper).clickable(onClick = onTime)
                            .padding(horizontal = 14.dp, vertical = 8.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Icon(OrielIcons.Clock, null, tint = Palette.colors.ink, modifier = Modifier.size(16.dp))
                        Spacer(Modifier.width(8.dp))
                        Text("Every day at $time", style = MaterialTheme.typography.labelLarge)
                    }
                }
            }
        }
    }
}

@Composable
private fun WhereItShows(screen: Screen, onPick: (Screen) -> Unit) {
    Heading("Make it yours", "Where should the wallpaper show? You can change this later in Settings.")
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Choice("Home and lock screen", "The same photo everywhere you look.", screen == Screen.Both, { onPick(Screen.Both) })
        Choice("Home screen", "Your lock screen stays as it is.", screen == Screen.Home, { onPick(Screen.Home) })
        Choice("Lock screen", "A new photo greets you before you unlock.", screen == Screen.Lock, { onPick(Screen.Lock) })
    }
}

/** A card that is one of a few answers: the chosen one gets an ink edge. */
@Composable
private fun Choice(title: String, text: String, selected: Boolean, onClick: () -> Unit, extra: @Composable () -> Unit = {}) {
    val c = Palette.colors
    val edge by animateColorAsState(if (selected) c.ink else c.surface, tween(200), label = "choiceEdge")
    val shape = RoundedCornerShape(20.dp)
    Column(
        Modifier.fillMaxWidth().clip(shape).background(c.surface).border(2.dp, edge, shape).clickable(onClick = onClick)
            .padding(horizontal = 18.dp, vertical = 14.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(title, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
                Text(text, style = MaterialTheme.typography.bodyMedium, color = c.muted)
            }
            if (selected) Icon(OrielIcons.Check, null, tint = c.ink, modifier = Modifier.size(20.dp))
        }
        extra()
    }
}

@Composable
private fun Done(picked: List<String>, interval: Int, time: String, screen: Screen) {
    Spacer(Modifier.height(24.dp))
    Heading("You're all set", "Here's what Oriel will do. Let's find your first wallpaper.")
    val names = picked.map { q -> q.replaceFirstChar { it.uppercase() } }
    Group(Modifier.padding(horizontal = 0.dp)) {
        SettingRow("Interests", if (names.size <= 3) names.joinToString(", ") else "${names.take(3).joinToString(", ")} and ${names.size - 3} more",
            OrielIcons.Heart)
        RowDivider()
        SettingRow("New photo", if (interval == 0) "Every day at $time" else INTERVALS.firstOrNull { it.first == interval }?.second ?: "",
            OrielIcons.Calendar)
        RowDivider()
        SettingRow("Shown on", when (screen) { Screen.Both -> "Home and lock screen"; Screen.Home -> "Home screen"; Screen.Lock -> "Lock screen" },
            OrielIcons.Phone)
    }
}
