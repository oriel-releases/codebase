package uz.akbarjon.oriel.ui

import androidx.compose.ui.graphics.graphicsLayer
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.Sharing
import uz.akbarjon.oriel.data.Follower
import uz.akbarjon.oriel.data.OrielException
import uz.akbarjon.oriel.data.Source

private enum class FriendsSheet { Follow, Followers, StopSharing, Unfollow }

/** Friends, like the desktop apps: whose wallpaper you follow, sharing yours with a code, and who follows you. */
@Composable
/** Friends, opened from Settings: [onBack] returns there. */
fun FriendsScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val snackbar = LocalSnackbar.current
    val settings by Oriel.store.settings.collectAsState()
    val state by Oriel.store.state.collectAsState()
    var sheet by remember { mutableStateOf<FriendsSheet?>(null) }
    var busy by remember { mutableStateOf(false) }
    var followers by remember { mutableStateOf<List<Follower>?>(null) }
    val code = settings.myFollowCode

    LaunchedEffect(code) { followers = if (code != null) Sharing.followers() else emptyList() }

    fun run(block: suspend () -> Unit) {
        if (busy) return
        busy = true
        scope.launch {
            try {
                block()
            } catch (e: OrielException) {
                Oriel.scope.launch(Dispatchers.Main) { snackbar.showSnackbar(e.message ?: "Something went wrong") }
            } finally {
                busy = false
            }
        }
    }

    androidx.activity.compose.BackHandler(onBack = onBack)
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).readableWidth()) {
        Row(Modifier.fillMaxWidth().height(72.dp).padding(start = 8.dp, end = 24.dp, top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            androidx.compose.material3.IconButton(onBack) {
                Icon(OrielIcons.ChevronRight, "Back", Modifier.size(24.dp).graphicsLayer { rotationZ = 180f })
            }
            Text("Friends", style = MaterialTheme.typography.headlineMedium)
        }

        // Following someone: their wallpaper is on this phone (and the account's other devices).
        if (settings.source == Source.Friend) {
            Group {
                Row(Modifier.fillMaxWidth().padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                    ProfilePicture(settings.followPicture, settings.followName ?: "?", 48.dp)
                    Spacer(Modifier.width(14.dp))
                    Column(Modifier.weight(1f)) {
                        Text("Following ${settings.followName ?: "a friend"}", style = MaterialTheme.typography.titleMedium)
                        state.followProblem?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error) }
                    }
                    TextAction("Unfollow", { sheet = FriendsSheet.Unfollow }, color = MaterialTheme.colorScheme.error)
                }
            }
            Spacer(Modifier.height(8.dp))
        }

        SectionLabel("Share my wallpaper")
        Group {
            AnimatedContent(code, transitionSpec = { fadeIn() togetherWith fadeOut() }, label = "code") { c ->
                if (c == null) {
                    Column(Modifier.fillMaxWidth().padding(20.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                        Icon(OrielIcons.Users, null, modifier = Modifier.size(36.dp), tint = Palette.colors.muted)
                        Spacer(Modifier.height(16.dp))
                        PrimaryButton(if (busy) "Getting a code…" else "Get my code", { run { Sharing.start() } },
                            Modifier.fillMaxWidth(), busy = busy)
                    }
                } else {
                    Column(Modifier.fillMaxWidth().padding(20.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(Sharing.format(c), fontSize = 32.sp, fontWeight = FontWeight.Bold, letterSpacing = 3.sp)
                        Spacer(Modifier.height(18.dp))
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            PrimaryButton("Share link", { shareLink(context, c) }, Modifier.weight(1f),
                                icon = { Icon(OrielIcons.Send, null, Modifier.size(18.dp)) })
                            RoundIconButton(OrielIcons.Copy, "Copy code", {
                                copy(context, Sharing.format(c))   // Android shows its own "copied"
                            })
                        }
                    }
                }
            }
            if (code != null) {
                RowDivider(16.dp)
                SettingRow("Followers", followers?.size?.toString() ?: "…", OrielIcons.Users, onClick = { sheet = FriendsSheet.Followers })
                RowDivider()
                SettingRow("Stop sharing", icon = OrielIcons.Close, titleColor = MaterialTheme.colorScheme.error,
                    onClick = { sheet = FriendsSheet.StopSharing })
            }
        }

        SectionLabel("Follow a friend")
        Group {
            SettingRow("Enter a friend's code", icon = OrielIcons.User, onClick = { sheet = FriendsSheet.Follow })
        }
        BottomBarSpacer()
    }

    val close = { sheet = null }
    when (sheet) {
        FriendsSheet.Follow -> FollowSheet(close)
        FriendsSheet.Followers -> FollowersSheet(followers, close) { follower ->
            run {
                Sharing.removeFollower(follower.uid)
                followers = followers?.filter { it.uid != follower.uid }
            }
        }
        FriendsSheet.StopSharing -> OrielSheet(close, "Stop sharing?") {
            Text("Your code stops working and everyone following you stops seeing your wallpaper.",
                style = MaterialTheme.typography.bodyLarge, color = Palette.colors.muted, modifier = Modifier.padding(horizontal = 24.dp))
            SheetActions("Stop sharing", { sheet = null; run { Sharing.stop() } }, "Cancel", close, destructive = true, busy = busy)
        }
        FriendsSheet.Unfollow -> OrielSheet(close, "Unfollow ${settings.followName ?: "your friend"}?") {
            Text("Oriel picks your wallpapers again, on your schedule.",
                style = MaterialTheme.typography.bodyLarge, color = Palette.colors.muted, modifier = Modifier.padding(horizontal = 24.dp))
            SheetActions("Unfollow", { sheet = null; run { Oriel.following.stop() } }, "Cancel", close, destructive = true, busy = busy)
        }
        null -> Unit
    }
}

@Composable
private fun Initial(name: String, size: Int) {
    Box(Modifier.size(size.dp).clip(CircleShape).background(Palette.colors.ink), contentAlignment = Alignment.Center) {
        Text(name.take(1).uppercase(), style = MaterialTheme.typography.titleMedium, color = Palette.colors.paper)
    }
}

@Composable
private fun FollowersSheet(followers: List<Follower>?, onDismiss: () -> Unit, onRemove: (Follower) -> Unit) {
    OrielSheet(onDismiss, "Followers") {
        when {
            followers == null -> Text("Couldn't load them right now.", color = Palette.colors.muted, modifier = Modifier.padding(24.dp))
            followers.isEmpty() -> Text("No one yet. Share your code and friends who follow show up here.",
                color = Palette.colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 8.dp))
            else -> LazyColumn(Modifier.heightIn(max = 420.dp)) {
                items(followers, key = { it.uid }) { f ->
                    Row(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Initial(f.name, 40)
                        Spacer(Modifier.width(14.dp))
                        Column(Modifier.weight(1f)) {
                            Text(f.name, style = MaterialTheme.typography.bodyLarge)
                            if (f.since > 0) Text("Since ${friendlyTime(f.since)}", style = MaterialTheme.typography.bodySmall, color = Palette.colors.muted)
                        }
                        TextAction("Remove", { onRemove(f) }, color = MaterialTheme.colorScheme.error)
                    }
                }
            }
        }
    }
}

/** Following a friend by their code (moved here from Settings, like the desktop apps' Friends page). */
@Composable
fun FollowSheet(onDismiss: () -> Unit) {
    val scope = rememberCoroutineScope()
    val snackbar = LocalSnackbar.current
    var code by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    OrielSheet(onDismiss, "Follow a friend", dismissible = !busy) {
        OrielTextField(code, { code = it.take(12); error = null }, Modifier.padding(horizontal = 16.dp), placeholder = "K7MQ-2XPA",
            leadingIcon = OrielIcons.User, isError = error != null,
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters))
        error?.let {
            Text(it, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(start = 24.dp, end = 24.dp, top = 10.dp))
        }
        val closeSheet = LocalSheetClose.current
        SheetActions(if (busy) "Following…" else "Follow", {
            busy = true
            scope.launch {
                try {
                    Oriel.following.follow(code)   // Friends then shows who's followed
                    closeSheet?.invoke(onDismiss) ?: onDismiss()
                } catch (e: OrielException) {
                    error = e.message
                } finally {
                    busy = false
                }
            }
        }, "Cancel", onDismiss, busy = busy, enabled = code.isNotBlank(), closes = false)
    }
}

private fun shareLink(context: Context, code: String) {
    val text = "Follow my wallpaper on Oriel: ${Sharing.link(code)}"
    val send = Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, text)
    context.startActivity(Intent.createChooser(send, "Share your code").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
}

private fun copy(context: Context, text: String) {
    context.getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText("Oriel code", text))
}
