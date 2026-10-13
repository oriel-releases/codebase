package uz.akbarjon.oriel.ui

import androidx.compose.animation.SizeTransform
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.InteractionSource
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.SheetValue
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.material3.ripple
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.R
import uz.akbarjon.oriel.data.HistoryEntry
import uz.akbarjon.oriel.data.Photo

/*
 * Oriel's look: black and white. Ink on paper, white cards, one weight of hairline. Color comes only from the photos
 * (and a faint wash of the wallpaper's accent behind Home); the controls themselves stay monochrome.
 */

/** Short messages at the bottom of the screen ("Wallpaper set"). */
val LocalSnackbar = staticCompositionLocalOf { SnackbarHostState() }

/** Android's share sheet with a line of text (a link). */
fun shareText(context: Context, text: String, title: String = "Share") {
    val send = Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, text)
    context.startActivity(Intent.createChooser(send, title).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
}

fun openUrl(context: Context, url: String) {
    try {
        context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    } catch (_: ActivityNotFoundException) {
    }
}

/** The floating navigation bar's own height, and its gap from the screen's edge. */
val NavBarHeight = 64.dp
val NavBarMargin = 12.dp

/** True while the phone is on its side: the navigation bar stands on the right edge instead of the bottom. */
val LocalNavOnSide = staticCompositionLocalOf { false }

/** Space at the end of a screen so its last item clears the navigation bar. */
@Composable
fun bottomBarSpace(): Dp = WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding() +
    if (LocalNavOnSide.current) 16.dp else NavBarHeight + NavBarMargin * 2

@Composable
fun BottomBarSpacer() = Spacer(Modifier.height(bottomBarSpace()))

/** The current wallpaper (the last photo in History), refreshed when History changes. */
@Composable
fun currentPhoto(): Photo? {
    val version by Oriel.history.version.collectAsState()
    val latest by produceState<HistoryEntry?>(null, version) { value = Oriel.history.latest() }
    return latest?.photo
}

// ---- Brand ----

@Composable
fun Logo(size: Dp = 40.dp, modifier: Modifier = Modifier, tint: Color = Palette.colors.ink) {
    Image(painterResource(R.drawable.ic_logo), contentDescription = null, modifier = modifier.size(size), colorFilter = ColorFilter.tint(tint))
}

@Composable
fun BetaBadge(modifier: Modifier = Modifier) {
    val c = Palette.colors
    Text(
        "BETA", style = MaterialTheme.typography.labelSmall, color = c.paper,
        modifier = modifier.clip(CircleShape).background(c.ink).padding(horizontal = 7.dp, vertical = 2.dp),
    )
}

/** The account's Google picture in a circle, or its initial when there's none. */
@Composable
fun ProfilePicture(url: String?, name: String, size: Dp, ring: Color = Color.Transparent) {
    val c = Palette.colors
    Box(Modifier.size(size).clip(CircleShape).background(c.ink).border(1.dp, ring, CircleShape), contentAlignment = Alignment.Center) {
        Text(name.take(1).uppercase(), style = MaterialTheme.typography.titleMedium, color = c.paper)
        if (!url.isNullOrBlank()) RemoteImage(url, null, Modifier.fillMaxSize(), placeholder = Color.Transparent)
    }
}

// ---- Motion ----

/** Shrinks a little while pressed and springs back: the one micro-interaction for anything tappable. */
fun Modifier.pressScale(interaction: InteractionSource, pressed: Float = 0.97f): Modifier = composed {
    val down by interaction.collectIsPressedAsState()
    val scale by animateFloatAsState(if (down) pressed else 1f, spring(dampingRatio = 0.5f, stiffness = 800f), label = "press")
    graphicsLayer { scaleX = scale; scaleY = scale }
}

/** Clickable with the press spring (and a ripple unless [ripple] is false). */
fun Modifier.springClick(enabled: Boolean = true, pressed: Float = 0.97f, ripple: Boolean = true, onClick: () -> Unit): Modifier = composed {
    val interaction = remember { MutableInteractionSource() }
    pressScale(interaction, pressed).clickable(interaction, if (ripple) ripple() else null, enabled = enabled, onClick = onClick)
}

// ---- Layout ----

/** A screen's big title, with an optional action on the right. No subtitles. */
@Composable
fun ScreenTitle(title: String, action: (@Composable RowScope.() -> Unit)? = null) {
    Row(
        Modifier.fillMaxWidth().height(72.dp).padding(start = 24.dp, end = 16.dp, top = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(title, style = MaterialTheme.typography.headlineMedium, modifier = Modifier.weight(1f))
        action?.invoke(this)
    }
}

@Composable
fun SectionLabel(text: String, modifier: Modifier = Modifier) {
    Text(
        text, modifier = modifier.padding(start = 32.dp, end = 24.dp, top = 24.dp, bottom = 8.dp),
        style = MaterialTheme.typography.labelMedium, color = Palette.colors.muted,
    )
}

/** A rounded white card of rows. */
@Composable
fun Group(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Column(
        modifier.padding(horizontal = 16.dp).fillMaxWidth().clip(RoundedCornerShape(22.dp)).background(Palette.colors.surface),
        content = content,
    )
}

/** A hairline between rows of a [Group], starting after the icon. */
@Composable
fun RowDivider(inset: Dp = 52.dp) = HorizontalDivider(Modifier.padding(start = inset), thickness = 0.5.dp, color = Palette.colors.line)

/** One row of a [Group]: icon, title, the current value on the right, then a chevron when it opens something. */
@Composable
fun SettingRow(
    title: String, value: String? = null, icon: ImageVector? = null, onClick: (() -> Unit)? = null,
    titleColor: Color = Palette.colors.ink, iconTint: Color = titleColor,
    leading: (@Composable () -> Unit)? = null, trailing: @Composable (() -> Unit)? = null,
) {
    Row(
        Modifier.fillMaxWidth().height(56.dp).let { if (onClick != null) it.clickable(onClick = onClick) else it }.padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        when {
            leading != null -> { leading(); Spacer(Modifier.width(14.dp)) }
            icon != null -> { Icon(icon, null, tint = iconTint, modifier = Modifier.size(21.dp)); Spacer(Modifier.width(16.dp)) }
        }
        Text(title, style = MaterialTheme.typography.bodyLarge, color = titleColor, maxLines = 1, overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f))
        if (value != null) {
            Spacer(Modifier.width(12.dp))
            AnimatedContent(value, Modifier.widthIn(max = 200.dp), contentAlignment = Alignment.CenterEnd,
                transitionSpec = { fadeIn(tween(200)) togetherWith fadeOut(tween(100)) }, label = "value") {
                Text(it, style = MaterialTheme.typography.bodyMedium, color = Palette.colors.muted, maxLines = 1,
                    overflow = TextOverflow.Ellipsis, textAlign = TextAlign.End)
            }
        }
        if (trailing != null) {
            Spacer(Modifier.width(12.dp))
            trailing()
        } else if (onClick != null) {
            Spacer(Modifier.width(2.dp))
            Icon(OrielIcons.ChevronRight, null, tint = Palette.colors.muted.copy(alpha = 0.6f), modifier = Modifier.size(18.dp))
        }
    }
}

@Composable
fun SwitchRow(title: String, icon: ImageVector? = null, checked: Boolean, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    val haptics = LocalHapticFeedback.current
    val toggle = { on: Boolean ->
        haptics.performHapticFeedback(if (on) HapticFeedbackType.ToggleOn else HapticFeedbackType.ToggleOff)
        onChange(on)
    }
    SettingRow(title, icon = icon, onClick = if (enabled) ({ toggle(!checked) }) else null) { OrielSwitch(checked, enabled, toggle) }
}

@Composable
fun OrielSwitch(checked: Boolean, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    val c = Palette.colors
    Switch(
        checked, onCheckedChange = onChange, enabled = enabled,
        colors = SwitchDefaults.colors(
            checkedThumbColor = c.paper, checkedTrackColor = c.ink, checkedBorderColor = Color.Transparent,
            uncheckedThumbColor = c.paper, uncheckedTrackColor = c.line, uncheckedBorderColor = Color.Transparent,
        ),
    )
}

// ---- Buttons ----

/** The main action: a solid ink pill with paper text (Home passes white on black instead). */
@Composable
fun PrimaryButton(
    text: String, onClick: () -> Unit, modifier: Modifier = Modifier, busy: Boolean = false, enabled: Boolean = true,
    container: Color = Palette.colors.ink, content: Color = Palette.colors.paper,
    icon: (@Composable () -> Unit)? = null,
) {
    val interaction = remember { MutableInteractionSource() }
    Button(
        onClick, enabled = enabled && !busy, shape = CircleShape, interactionSource = interaction,
        modifier = modifier.height(56.dp).pressScale(interaction),
        contentPadding = PaddingValues(horizontal = 24.dp), elevation = null,
        colors = ButtonDefaults.buttonColors(
            containerColor = container, contentColor = content,
            disabledContainerColor = container.copy(alpha = 0.4f), disabledContentColor = content.copy(alpha = 0.9f),
        ),
    ) {
        AnimatedContent(busy, transitionSpec = { (fadeIn() + scaleIn(initialScale = 0.6f)) togetherWith fadeOut() }, label = "busy") { b ->
            when {
                b -> Row(verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(Modifier.size(18.dp), color = content, strokeWidth = 2.dp)
                    Spacer(Modifier.width(10.dp))
                }
                icon != null -> Row(verticalAlignment = Alignment.CenterVertically) { icon(); Spacer(Modifier.width(10.dp)) }
                else -> Spacer(Modifier.width(0.dp))
            }
        }
        SlidingText(text)
    }
}

/** A label whose words slide: the old ones up and out, the new ones in from below. */
@Composable
fun SlidingText(text: String, style: androidx.compose.ui.text.TextStyle = MaterialTheme.typography.labelLarge, color: Color = Color.Unspecified) {
    AnimatedContent(
        text,
        transitionSpec = {
            (slideInVertically(tween(220)) { it / 2 } + fadeIn(tween(220))) togetherWith
                (slideOutVertically(tween(160)) { -it / 2 } + fadeOut(tween(120))) using SizeTransform(clip = false)
        },
        label = "label",
    ) { Text(it, style = style, color = color, maxLines = 1) }
}

/** A second action: white card, ink text. */
@Composable
fun SecondaryButton(
    text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true,
    icon: (@Composable () -> Unit)? = null,
) {
    val c = Palette.colors
    val interaction = remember { MutableInteractionSource() }
    Button(
        onClick, enabled = enabled, shape = CircleShape, interactionSource = interaction,
        modifier = modifier.height(56.dp).pressScale(interaction).border(1.dp, c.line, CircleShape),
        contentPadding = PaddingValues(horizontal = 20.dp), elevation = null,
        colors = ButtonDefaults.buttonColors(
            containerColor = c.surface, contentColor = c.ink, disabledContainerColor = c.surface, disabledContentColor = c.muted,
        ),
    ) {
        if (icon != null) {
            icon()
            Spacer(Modifier.width(10.dp))
        }
        Text(text, style = MaterialTheme.typography.labelLarge)
    }
}

/** A quiet text action ("Forgot password?"). */
@Composable
fun TextAction(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, color: Color = Palette.colors.ink, enabled: Boolean = true) {
    Text(
        text, style = MaterialTheme.typography.labelLarge, color = if (enabled) color else color.copy(alpha = 0.4f),
        modifier = modifier.clip(RoundedCornerShape(12.dp)).springClick(enabled, onClick = onClick).padding(horizontal = 12.dp, vertical = 10.dp),
    )
}

/** A round icon button on a white card. */
@Composable
fun RoundIconButton(
    icon: ImageVector, description: String?, onClick: () -> Unit, modifier: Modifier = Modifier,
    tint: Color = Palette.colors.ink, background: Color = Palette.colors.surface, size: Dp = 56.dp, enabled: Boolean = true,
) {
    Box(
        modifier.size(size).clip(CircleShape).background(background).border(1.dp, Palette.colors.line, CircleShape)
            .springClick(enabled, pressed = 0.9f, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { Icon(icon, description, tint = if (enabled) tint else tint.copy(alpha = 0.4f), modifier = Modifier.size(22.dp)) }
}

// ---- Inputs ----

/** A filled text field: a white pill, no underline. */
@Composable
fun OrielTextField(
    value: String, onValueChange: (String) -> Unit, modifier: Modifier = Modifier, placeholder: String? = null,
    leadingIcon: ImageVector? = null, trailing: (@Composable () -> Unit)? = null, enabled: Boolean = true, isError: Boolean = false,
    visualTransformation: VisualTransformation = VisualTransformation.None,
    keyboardOptions: KeyboardOptions = KeyboardOptions.Default, keyboardActions: KeyboardActions = KeyboardActions.Default,
    background: Color = Palette.colors.surface,
) {
    val c = Palette.colors
    TextField(
        value, onValueChange, modifier.fillMaxWidth(), enabled = enabled, singleLine = true, isError = isError,
        placeholder = placeholder?.let { { Text(it, color = c.muted) } },
        leadingIcon = leadingIcon?.let { { Icon(it, null, tint = c.muted, modifier = Modifier.size(20.dp)) } },
        trailingIcon = trailing,
        shape = CircleShape, visualTransformation = visualTransformation,
        keyboardOptions = keyboardOptions, keyboardActions = keyboardActions,
        textStyle = MaterialTheme.typography.bodyLarge,
        colors = TextFieldDefaults.colors(
            focusedContainerColor = background, unfocusedContainerColor = background, disabledContainerColor = background,
            errorContainerColor = background, focusedTextColor = c.ink, unfocusedTextColor = c.ink,
            focusedIndicatorColor = Color.Transparent, unfocusedIndicatorColor = Color.Transparent,
            disabledIndicatorColor = Color.Transparent, errorIndicatorColor = Color.Transparent,
            cursorColor = c.ink,
        ),
    )
}

// ---- Chips and segments ----

/** A pill that can be on (ink) or off (white), with a spring on tap. */
@Composable
fun ToggleChip(label: String, selected: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier, leading: String? = null) {
    val c = Palette.colors
    val haptics = LocalHapticFeedback.current
    val bg by animateColorAsState(if (selected) c.ink else c.surface, tween(200), label = "chip")
    val fg by animateColorAsState(if (selected) c.paper else c.ink, tween(200), label = "chipText")
    Row(
        modifier.clip(CircleShape).background(bg)
            .springClick(pressed = 0.92f) { haptics.performHapticFeedback(HapticFeedbackType.SegmentTick); onClick() }
            .padding(horizontal = 16.dp, vertical = 10.dp)
            .animateContentSize(spring(dampingRatio = 0.7f, stiffness = 600f)),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (leading != null) {
            Text(leading, style = MaterialTheme.typography.bodyMedium)
            Spacer(Modifier.width(8.dp))
        }
        Text(label, style = MaterialTheme.typography.bodyMedium, color = fg, fontWeight = FontWeight.Medium)
        AnimatedVisibility(selected, enter = scaleIn(spring(dampingRatio = 0.45f)) + fadeIn(), exit = scaleOut() + fadeOut()) {
            Row {
                Spacer(Modifier.width(6.dp))
                Icon(OrielIcons.Check, null, tint = fg, modifier = Modifier.size(15.dp))
            }
        }
    }
}

/** Options in one track, with the ink selection sliding between them. */
@Composable
fun <T> Segmented(
    options: List<Pair<T, String>>, selected: T, onSelect: (T) -> Unit, modifier: Modifier = Modifier,
    track: Color = Palette.colors.surface,
) {
    val c = Palette.colors
    val haptics = LocalHapticFeedback.current
    // A white track on the grey page, and an ink pill for the picked option (inverted in dark mode): clearly apart.
    BoxWithConstraints(modifier.height(44.dp).clip(CircleShape).background(track).border(1.dp, c.line, CircleShape).padding(4.dp)) {
        val w = maxWidth / options.size
        val index = options.indexOfFirst { it.first == selected }.coerceAtLeast(0)
        val x by animateDpAsState(w * index, spring(dampingRatio = 0.8f, stiffness = 600f), label = "segment")
        Box(Modifier.offset(x = x).width(w).fillMaxHeight().clip(CircleShape).background(c.ink))
        Row(Modifier.fillMaxSize()) {
            options.forEachIndexed { i, (value, label) ->
                val on = i == index
                val fg by animateColorAsState(if (on) c.paper else c.muted, label = "segmentText")
                Box(
                    Modifier.weight(1f).fillMaxHeight().clip(CircleShape)
                        .clickable(remember { MutableInteractionSource() }, null) {
                            if (!on) { haptics.performHapticFeedback(HapticFeedbackType.SegmentTick); onSelect(value) }
                        },
                    contentAlignment = Alignment.Center,
                ) { Text(label, style = MaterialTheme.typography.labelMedium, color = fg) }
            }
        }
    }
}

// ---- Sheets: every dialog in the app ----

/** A sheet that rises from the bottom, rounded only on top. [title] sits on top. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OrielSheet(onDismiss: () -> Unit, title: String? = null, dismissible: Boolean = true, content: @Composable ColumnScope.() -> Unit) {
    val c = Palette.colors
    ModalBottomSheet(
        onDismissRequest = { if (dismissible) onDismiss() },
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true, confirmValueChange = { dismissible || it != SheetValue.Hidden }),
        shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp),
        containerColor = c.paper, contentColor = c.ink, scrimColor = c.scrim, tonalElevation = 0.dp,
        dragHandle = {
            Box(Modifier.padding(top = 10.dp, bottom = 8.dp).size(width = 36.dp, height = 4.dp).clip(CircleShape).background(c.muted.copy(alpha = 0.35f)))
        },
    ) {
        Column(Modifier.fillMaxWidth().navigationBarsPadding().padding(bottom = 12.dp)) {
            if (title != null) {
                Text(title, style = MaterialTheme.typography.titleLarge, modifier = Modifier.padding(start = 24.dp, end = 24.dp, top = 4.dp, bottom = 16.dp))
            }
            content()
        }
    }
}

/** One choice in a sheet: a row with a round check on the picked one. */
@Composable
fun SheetOption(label: String, selected: Boolean, onClick: () -> Unit, icon: ImageVector? = null) {
    val c = Palette.colors
    Row(
        Modifier.padding(horizontal = 12.dp).fillMaxWidth().height(56.dp).clip(CircleShape)
            .background(if (selected) c.surface else Color.Transparent).clickable(onClick = onClick).padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon != null) {
            Icon(icon, null, tint = c.ink, modifier = Modifier.size(22.dp))
            Spacer(Modifier.width(14.dp))
        }
        Text(label, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f),
            fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal)
        AnimatedVisibility(selected, enter = scaleIn(spring(dampingRatio = 0.45f)) + fadeIn(), exit = fadeOut()) {
            Box(Modifier.size(24.dp).clip(CircleShape).background(c.ink), contentAlignment = Alignment.Center) {
                Icon(OrielIcons.Check, null, tint = c.paper, modifier = Modifier.size(14.dp))
            }
        }
    }
}

/** The buttons at the bottom of a sheet: the main action, and an optional quiet one beside it. */
@Composable
fun SheetActions(
    primary: String, onPrimary: () -> Unit, secondary: String? = null, onSecondary: (() -> Unit)? = null,
    busy: Boolean = false, enabled: Boolean = true, destructive: Boolean = false,
) {
    Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 20.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        if (secondary != null && onSecondary != null) SecondaryButton(secondary, onSecondary, Modifier.weight(1f), enabled = !busy)
        if (destructive) {
            val interaction = remember { MutableInteractionSource() }
            Button(
                onPrimary, Modifier.weight(1f).height(56.dp).pressScale(interaction), enabled = enabled && !busy,
                shape = CircleShape, interactionSource = interaction, elevation = null,
                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error, contentColor = Color.White),
            ) { Text(primary, style = MaterialTheme.typography.labelLarge) }
        } else {
            PrimaryButton(primary, onPrimary, Modifier.weight(1f), busy = busy, enabled = enabled)
        }
    }
}

// ---- Credit and time ----

/** "Photo by Name on Unsplash", both links, as Unsplash's guidelines ask. */
@Composable
fun Credit(photo: Photo, context: Context, modifier: Modifier = Modifier, color: Color = Palette.colors.muted) {
    Row(modifier.widthIn(max = 360.dp), horizontalArrangement = Arrangement.Start) {
        // Only the name gives way when it's tight (ellipsis); the rest never wraps.
        Text("Photo by ", style = MaterialTheme.typography.bodySmall, color = color, maxLines = 1, softWrap = false)
        Text(
            photo.photographer, style = MaterialTheme.typography.bodySmall, color = color, fontWeight = FontWeight.SemiBold,
            maxLines = 1, overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f, fill = false).clickable { openUrl(context, photo.photographerUrl) },
        )
        Text(
            buildAnnotatedString {
                append(" on ")
                withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append("Unsplash") }
            },
            style = MaterialTheme.typography.bodySmall, color = color, maxLines = 1, softWrap = false,
            modifier = Modifier.clickable { openUrl(context, photo.pageUrl) },
        )
    }
}

fun friendlyTime(epochMs: Long): String {
    val zone = java.time.ZoneId.systemDefault()
    val at = java.time.Instant.ofEpochMilli(epochMs).atZone(zone)
    val today = java.time.LocalDate.now(zone)
    val time = at.toLocalTime().format(java.time.format.DateTimeFormatter.ofLocalizedTime(java.time.format.FormatStyle.SHORT))
    return when (at.toLocalDate()) {
        today -> "Today, $time"
        today.minusDays(1) -> "Yesterday, $time"
        today.plusDays(1) -> "Tomorrow, $time"
        else -> at.format(java.time.format.DateTimeFormatter.ofLocalizedDateTime(java.time.format.FormatStyle.MEDIUM, java.time.format.FormatStyle.SHORT))
    }
}
