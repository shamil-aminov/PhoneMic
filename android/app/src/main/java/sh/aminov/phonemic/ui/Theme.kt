package sh.aminov.phonemic.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color

/**
 * Built for OLED screens: the background is true black, so everything that is
 * not content costs no light at all, and the wave glows out of nothing.
 * The PC app uses the same values (desktop/src/PhoneMic/App.xaml).
 */
object Palette {
    val Background = Color(0xFF000000)
    val Island = Color(0xFF0B0C0F)
    val IslandBorder = Color(0xFF1B1E24)
    val Control = Color(0xFF16191E)
    val Text = Color(0xFFF2F4F7)
    val TextDim = Color(0xFF8B93A1)
    val TextFaint = Color(0xFF4A515C)

    val Cyan = Color(0xFF22D3EE)
    val Blue = Color(0xFF60A5FA)
    val Violet = Color(0xFFA78BFA)

    /** Microphone live. */
    val On = Cyan

    /** Microphone off. */
    val Off = Color(0xFFF43F5E)

    /** Looking for the PC. */
    val Pending = Color(0xFFFBBF24)

    val WaveColors = listOf(Cyan, Blue, Violet, Blue, Cyan)
    val Accent = Brush.horizontalGradient(listOf(Cyan, Blue, Violet))
}

@Composable
fun PhoneMicTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = Palette.Cyan,
            onPrimary = Color.Black,
            background = Palette.Background,
            onBackground = Palette.Text,
            surface = Palette.Island,
            onSurface = Palette.Text,
            surfaceVariant = Palette.Control,
            onSurfaceVariant = Palette.TextDim,
            surfaceContainerLow = Palette.Island,
            outline = Palette.IslandBorder,
            error = Palette.Off,
        ),
        content = content,
    )
}
