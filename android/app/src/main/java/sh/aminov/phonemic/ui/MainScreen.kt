package sh.aminov.phonemic.ui

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import sh.aminov.phonemic.R
import sh.aminov.phonemic.audio.LinkState
import sh.aminov.phonemic.data.Pairing
import sh.aminov.phonemic.net.TransportKind

/** Everything the screen shows, in one place so previews and screenshot tests can build it. */
data class ScreenState(
    val running: Boolean,
    val link: LinkState,
    val level: Float,
    val micUnavailable: Boolean,
    val pairing: Pairing?,
    val noiseSuppression: Boolean,
)

@Composable
fun MainScreen(
    state: ScreenState,
    onToggle: () -> Unit,
    onScan: () -> Unit,
    onNoiseSuppressionChange: (Boolean) -> Unit,
    version: String,
    time: State<Float> = rememberFrameTime(),
    settingsInitiallyOpen: Boolean = false,
) {
    var settingsOpen by rememberSaveable { mutableStateOf(settingsInitiallyOpen) }
    val status = describe(state)

    Box(
        Modifier
            .fillMaxSize()
            .background(Palette.Background),
    ) {
        Column(
            Modifier
                .fillMaxSize()
                .safeDrawingPadding(),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), horizontalArrangement = Arrangement.End) {
                IconButton(onClick = { settingsOpen = true }) {
                    Icon(
                        painterResource(R.drawable.ic_settings),
                        contentDescription = stringResource(R.string.settings),
                        tint = Palette.TextFaint,
                        modifier = Modifier.size(22.dp),
                    )
                }
            }

            StatusHeader(status)

            Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                if (state.pairing == null) {
                    PairingPrompt(onScan)
                } else {
                    val haptics = LocalHapticFeedback.current
                    val toggleLabel = stringResource(if (state.running) R.string.turn_mic_off else R.string.turn_mic_on)
                    Wave(
                        mode = status.wave,
                        level = state.level,
                        time = time,
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(280.dp)
                            .clickable(
                                interactionSource = remember { MutableInteractionSource() },
                                indication = null,
                                role = Role.Switch,
                            ) {
                                haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                                onToggle()
                            }
                            .semantics { contentDescription = toggleLabel },
                    )
                }
            }

            if (state.pairing == null) {
                ScanButton(onScan, Modifier.padding(bottom = 28.dp))
            } else {
                ComputerIsland(state, onScan, Modifier.padding(bottom = 24.dp))
            }
        }
    }

    if (settingsOpen) {
        SettingsSheet(
            noiseSuppression = state.noiseSuppression,
            onNoiseSuppressionChange = onNoiseSuppressionChange,
            pcName = state.pairing?.pcName,
            onChangeComputer = {
                settingsOpen = false
                onScan()
            },
            version = version,
            onDismiss = { settingsOpen = false },
        )
    }
}

/** What the top of the screen says, and how the wave behaves. */
private data class Status(
    val title: String,
    val detail: String,
    val dot: Color,
    val detailColor: Color,
    val wave: WaveMode,
    val pulsing: Boolean,
)

@Composable
private fun describe(s: ScreenState): Status {
    val context = LocalContext.current
    return when {
        s.pairing == null -> Status(
            stringResource(R.string.status_no_pc), stringResource(R.string.status_no_pc_detail),
            Palette.TextFaint, Palette.TextDim, WaveMode.Off, false,
        )
        !s.running -> Status(
            stringResource(R.string.status_off), stringResource(R.string.status_off_detail),
            Palette.Off, Palette.TextDim, WaveMode.Off, false,
        )
        s.micUnavailable -> Status(
            stringResource(R.string.status_mic_unavailable), stringResource(R.string.mic_unavailable_detail),
            Palette.Off, Palette.Off, WaveMode.Waiting, false,
        )
        s.link is LinkState.Problem -> Status(
            stringResource(R.string.status_refused), Texts.refusal(context, s.link.refusal),
            Palette.Pending, Palette.Pending, WaveMode.Waiting, true,
        )
        s.link is LinkState.Connected -> Status(
            stringResource(R.string.status_live), stringResource(R.string.status_live_detail),
            Palette.On, Palette.TextDim, WaveMode.Live, false,
        )
        else -> Status(
            stringResource(R.string.status_connecting),
            stringResource(R.string.status_connecting_detail, Texts.pcName(context, s.pairing.pcName)),
            Palette.Pending, Palette.TextDim, WaveMode.Waiting, true,
        )
    }
}

@Composable
private fun StatusHeader(status: Status) {
    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(horizontal = 32.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            StatusDot(status.dot, status.pulsing)
            Spacer(Modifier.width(10.dp))
            AnimatedContent(
                status.title,
                transitionSpec = { fadeIn(tween(200)) togetherWith fadeOut(tween(200)) },
                label = "title",
            ) {
                Text(it, color = Palette.Text, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
            }
        }
        Spacer(Modifier.height(6.dp))
        Text(
            status.detail,
            color = status.detailColor,
            fontSize = 14.sp,
            textAlign = TextAlign.Center,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
            // Room for two lines, so the wave does not jump when the text changes.
            modifier = Modifier.height(48.dp),
        )
    }
}

@Composable
private fun StatusDot(color: Color, pulsing: Boolean) {
    val animated by animateColorAsState(color, tween(300), label = "dot")
    val pulse = if (pulsing) {
        val transition = rememberInfiniteTransition(label = "pulse")
        transition.animateFloat(0.35f, 1f, infiniteRepeatable(tween(700), RepeatMode.Reverse), label = "alpha").value
    } else {
        1f
    }
    Canvas(Modifier.size(18.dp)) {
        // Soft halo, then the dot itself.
        drawCircle(animated.copy(alpha = 0.22f * pulse), radius = size.minDimension / 2)
        drawCircle(animated.copy(alpha = pulse), radius = size.minDimension / 4.5f)
    }
}

@Composable
private fun ComputerIsland(state: ScreenState, onScan: () -> Unit, modifier: Modifier = Modifier) {
    val link = state.link
    val context = LocalContext.current
    val detail = when {
        !state.running -> stringResource(R.string.island_off)
        link is LinkState.Connected -> Texts.link(context, link)
        link is LinkState.Problem -> stringResource(R.string.island_refused)
        else -> stringResource(R.string.island_searching)
    }
    val changeLabel = stringResource(R.string.change_computer)
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = modifier
            .padding(horizontal = 40.dp)
            .widthIn(max = 320.dp)
            .fillMaxWidth()
            .clip(RoundedCornerShape(28.dp))
            .background(Palette.Island)
            .border(1.dp, Palette.IslandBorder, RoundedCornerShape(28.dp))
            .padding(start = 20.dp, end = 8.dp, top = 8.dp, bottom = 8.dp),
    ) {
        Icon(
            painterResource(R.drawable.ic_computer),
            contentDescription = null,
            tint = Palette.TextDim,
            modifier = Modifier.size(20.dp),
        )
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text(
                Texts.pcName(context, state.pairing?.pcName.orEmpty()),
                color = Palette.Text,
                fontSize = 15.sp,
                fontWeight = FontWeight.SemiBold,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(detail, color = Palette.TextDim, fontSize = 12.sp, maxLines = 1)
        }
        Box(
            contentAlignment = Alignment.Center,
            modifier = Modifier
                .size(44.dp)
                .clip(CircleShape)
                .background(Palette.Control)
                .clickable(onClick = onScan)
                .semantics { contentDescription = changeLabel },
        ) {
            Icon(painterResource(R.drawable.ic_qr), contentDescription = null, tint = Palette.Text, modifier = Modifier.size(20.dp))
        }
    }
}

@Composable
private fun PairingPrompt(onScan: () -> Unit) {
    Box(
        contentAlignment = Alignment.Center,
        modifier = Modifier
            .size(168.dp)
            .clip(RoundedCornerShape(36.dp))
            .border(
                1.dp,
                Brush.linearGradient(listOf(Palette.Cyan.copy(alpha = 0.5f), Palette.Violet.copy(alpha = 0.5f))),
                RoundedCornerShape(36.dp),
            )
            .clickable(onClick = onScan),
    ) {
        Icon(
            painterResource(R.drawable.ic_qr),
            contentDescription = stringResource(R.string.scan_qr),
            tint = Palette.TextFaint,
            modifier = Modifier.size(88.dp),
        )
    }
}

@Composable
private fun ScanButton(onScan: () -> Unit, modifier: Modifier = Modifier) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.Center,
        modifier = modifier
            .height(52.dp)
            .clip(RoundedCornerShape(26.dp))
            .background(Palette.Accent)
            .clickable(onClick = onScan)
            .padding(horizontal = 28.dp),
    ) {
        Icon(painterResource(R.drawable.ic_qr), contentDescription = null, tint = Color.Black, modifier = Modifier.size(20.dp))
        Spacer(Modifier.width(10.dp))
        Text(stringResource(R.string.scan_qr), color = Color.Black, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SettingsSheet(
    noiseSuppression: Boolean,
    onNoiseSuppressionChange: (Boolean) -> Unit,
    pcName: String?,
    onChangeComputer: () -> Unit,
    version: String,
    onDismiss: () -> Unit,
) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor = Palette.Island,
        scrimColor = Color.Black.copy(alpha = 0.6f),
    ) {
        Column(Modifier.padding(horizontal = 24.dp).padding(bottom = 24.dp)) {
            Text(stringResource(R.string.settings), color = Palette.Text, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
            Spacer(Modifier.height(20.dp))
            SheetRow(
                title = stringResource(R.string.noise_suppression),
                subtitle = stringResource(R.string.noise_suppression_detail),
                onClick = { onNoiseSuppressionChange(!noiseSuppression) },
            ) {
                Switch(
                    checked = noiseSuppression,
                    onCheckedChange = null,
                    colors = SwitchDefaults.colors(
                        checkedTrackColor = Palette.Cyan,
                        checkedThumbColor = Color.Black,
                        uncheckedTrackColor = Palette.Control,
                        uncheckedThumbColor = Palette.TextDim,
                        uncheckedBorderColor = Palette.IslandBorder,
                    ),
                )
            }
            SheetRow(
                title = stringResource(R.string.computer),
                subtitle = pcName?.let { Texts.pcName(LocalContext.current, it) } ?: stringResource(R.string.computer_none),
                onClick = onChangeComputer,
            ) {
                Text(stringResource(R.string.change), color = Palette.Cyan, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
            }
            Spacer(Modifier.height(16.dp))
            Text(stringResource(R.string.version, version), color = Palette.TextFaint, fontSize = 12.sp)
        }
    }
}

@Composable
private fun SheetRow(title: String, subtitle: String, onClick: () -> Unit, trailing: @Composable () -> Unit) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .clickable(onClick = onClick)
            .padding(vertical = 12.dp),
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, color = Palette.Text, fontSize = 16.sp)
            Text(subtitle, color = Palette.TextDim, fontSize = 13.sp)
        }
        Spacer(Modifier.width(12.dp))
        trailing()
    }
}

@Preview(widthDp = 390, heightDp = 844)
@Composable
private fun PreviewLive() {
    PhoneMicTheme {
        MainScreen(
            ScreenState(
                running = true,
                link = LinkState.Connected("Studio PC", TransportKind.WIFI, 12),
                level = 0.3f,
                micUnavailable = false,
                pairing = Pairing(listOf("192.168.1.2"), 50505, "T", "Studio PC"),
                noiseSuppression = false,
            ),
            onToggle = {}, onScan = {}, onNoiseSuppressionChange = {}, version = "1.0.0",
            time = remember { mutableStateOf(1.2f) },
        )
    }
}
