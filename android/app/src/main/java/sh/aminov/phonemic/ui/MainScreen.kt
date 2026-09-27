package sh.aminov.phonemic.ui

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
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
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import sh.aminov.phonemic.MicService
import sh.aminov.phonemic.R
import sh.aminov.phonemic.audio.LinkState
import sh.aminov.phonemic.data.Pairing
import kotlin.math.log10

@Composable
fun MainScreen(
    running: Boolean,
    link: LinkState,
    level: Float,
    micError: String?,
    pairing: Pairing?,
    noiseSuppression: Boolean,
    onToggle: () -> Unit,
    onScan: () -> Unit,
    onNoiseSuppressionChange: (Boolean) -> Unit,
) {
    Surface(color = Palette.Background, modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .safeDrawingPadding()
                .padding(horizontal = 24.dp, vertical = 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(
                "PhoneMic",
                style = MaterialTheme.typography.titleMedium,
                color = Palette.TextDim,
                fontWeight = FontWeight.SemiBold,
                modifier = Modifier.fillMaxWidth(),
            )

            Spacer(Modifier.weight(1f))

            val connected = running && link is LinkState.Connected
            MicButton(running = running, connected = connected, level = level, onClick = onToggle)

            Spacer(Modifier.height(36.dp))

            Text(
                text = when {
                    !running -> "Микрофон выключен"
                    connected -> "Микрофон работает"
                    else -> "Подключаюсь…"
                },
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.SemiBold,
                color = Palette.Text,
            )
            Spacer(Modifier.height(6.dp))
            val (detail, detailColor) = when {
                micError != null -> micError to Palette.Error
                !running && pairing == null -> "Сначала отсканируйте QR-код на компьютере" to Palette.TextDim
                !running -> "Нажмите, чтобы начать" to Palette.TextDim
                link is LinkState.Problem -> link.message to Palette.Warn
                else -> MicService.describe(link) to Palette.TextDim
            }
            Text(detail, color = detailColor, textAlign = TextAlign.Center, style = MaterialTheme.typography.bodyMedium)

            Spacer(Modifier.height(20.dp))
            LevelBar(level = if (running) level else 0f, active = connected)

            Spacer(Modifier.weight(1f))

            PcCard(pairing = pairing, onScan = onScan)
            Spacer(Modifier.height(12.dp))
            SettingRow(
                title = "Шумоподавление",
                subtitle = "Системное подавление шума и эха телефона",
                checked = noiseSuppression,
                onChange = onNoiseSuppressionChange,
            )
        }
    }
}

@Composable
private fun MicButton(running: Boolean, connected: Boolean, level: Float, onClick: () -> Unit) {
    val fill by animateColorAsState(
        when {
            connected -> Palette.Live
            running -> Palette.LiveDim
            else -> Palette.SurfaceHigh
        },
        tween(250), label = "fill",
    )
    val iconTint by animateColorAsState(
        when {
            connected -> Color(0xFF00210F)
            running -> Palette.Live
            else -> Palette.TextDim
        },
        tween(250), label = "icon",
    )
    // The halo breathes with the voice: louder speech, wider ring.
    val loudness = if (connected) ((20 * log10(level.coerceAtLeast(1e-4f)) + 60) / 60).coerceIn(0f, 1f) else 0f
    val halo by animateFloatAsState(loudness, tween(90), label = "halo")

    Box(contentAlignment = Alignment.Center, modifier = Modifier.size(260.dp)) {
        Canvas(Modifier.fillMaxSize()) {
            val base = size.minDimension / 2 * 0.72f
            if (running) {
                drawCircle(Palette.Live.copy(alpha = 0.10f + 0.15f * halo), radius = base + (size.minDimension / 2 - base) * halo)
                drawCircle(Palette.Live.copy(alpha = 0.35f), radius = base + 8.dp.toPx(), style = Stroke(1.5.dp.toPx()))
            } else {
                drawCircle(Palette.Outline, radius = base + 8.dp.toPx(), style = Stroke(1.5.dp.toPx()))
            }
        }
        Box(
            contentAlignment = Alignment.Center,
            modifier = Modifier
                .size(188.dp)
                .scale(1f + 0.03f * halo)
                .clip(CircleShape)
                .background(fill)
                .clickable(
                    interactionSource = MutableInteractionSource(),
                    indication = androidx.compose.material3.ripple(),
                    role = Role.Switch,
                    onClick = onClick,
                )
                .semantics { contentDescription = if (running) "Выключить микрофон" else "Включить микрофон" },
        ) {
            Icon(
                painterResource(if (running) R.drawable.ic_mic else R.drawable.ic_mic_off),
                contentDescription = null,
                tint = iconTint,
                modifier = Modifier.size(72.dp),
            )
        }
    }
}

@Composable
private fun LevelBar(level: Float, active: Boolean) {
    val db = 20 * log10(level.coerceAtLeast(1e-4f))
    val fraction = ((db + 60) / 60).coerceIn(0f, 1f)
    val animated by animateFloatAsState(fraction, tween(80), label = "level")
    Box(
        Modifier
            .width(220.dp)
            .height(6.dp)
            .clip(RoundedCornerShape(3.dp))
            .background(Palette.SurfaceHigh),
    ) {
        Box(
            Modifier
                .fillMaxWidth(animated)
                .height(6.dp)
                .clip(RoundedCornerShape(3.dp))
                .background(
                    when {
                        !active -> Palette.TextDim.copy(alpha = 0.5f)
                        db > -3 -> Palette.Error
                        else -> Palette.Live
                    }
                ),
        )
    }
}

@Composable
private fun PcCard(pairing: Pairing?, onScan: () -> Unit) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .background(Palette.Surface)
            .border(1.dp, Palette.Outline, RoundedCornerShape(16.dp))
            .padding(16.dp),
    ) {
        Icon(
            painterResource(R.drawable.ic_computer),
            contentDescription = null,
            tint = Palette.TextDim,
            modifier = Modifier.size(28.dp),
        )
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text("Компьютер", color = Palette.TextDim, style = MaterialTheme.typography.labelMedium)
            Text(
                pairing?.pcName ?: "Не выбран",
                color = Palette.Text,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
            )
        }
        if (pairing == null) {
            Button(onClick = onScan, colors = ButtonDefaults.buttonColors(containerColor = Palette.Live)) {
                Icon(painterResource(R.drawable.ic_qr), contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text("Сканировать")
            }
        } else {
            OutlinedButton(onClick = onScan) {
                Icon(painterResource(R.drawable.ic_qr), contentDescription = null, modifier = Modifier.size(18.dp), tint = Palette.Text)
                Spacer(Modifier.width(8.dp))
                Text("Сменить", color = Palette.Text)
            }
        }
    }
}

@Composable
private fun SettingRow(title: String, subtitle: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .background(Palette.Surface)
            .border(1.dp, Palette.Outline, RoundedCornerShape(16.dp))
            .clickable(role = Role.Switch) { onChange(!checked) }
            .padding(horizontal = 16.dp, vertical = 12.dp),
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, color = Palette.Text, style = MaterialTheme.typography.titleSmall)
            Text(subtitle, color = Palette.TextDim, fontSize = 13.sp)
        }
        Spacer(Modifier.width(12.dp))
        Switch(
            checked = checked,
            onCheckedChange = null,
            colors = SwitchDefaults.colors(
                checkedTrackColor = Palette.Live,
                checkedThumbColor = Color(0xFF00210F),
                uncheckedTrackColor = Palette.SurfaceHigh,
                uncheckedThumbColor = Palette.TextDim,
                uncheckedBorderColor = Palette.TextDim,
            ),
        )
    }
}

@Preview(showBackground = true, heightDp = 780, widthDp = 380)
@Composable
private fun PreviewConnected() {
    PhoneMicTheme {
        MainScreen(
            running = true,
            link = LinkState.Connected("DESKTOP", sh.aminov.phonemic.net.TransportKind.WIFI, 7),
            level = 0.2f,
            micError = null,
            pairing = Pairing(listOf("192.168.1.2"), 50505, "T", "DESKTOP"),
            noiseSuppression = false,
            onToggle = {}, onScan = {}, onNoiseSuppressionChange = {},
        )
    }
}
