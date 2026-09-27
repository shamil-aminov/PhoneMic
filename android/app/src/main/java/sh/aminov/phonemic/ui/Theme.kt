package sh.aminov.phonemic.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

object Palette {
    val Background = Color(0xFF0E1113)
    val Surface = Color(0xFF171B1E)
    val SurfaceHigh = Color(0xFF20262A)
    val Outline = Color(0xFF2C3438)
    val Text = Color(0xFFE8ECEE)
    val TextDim = Color(0xFF8A969C)
    val Live = Color(0xFF3DDC97)
    val LiveDim = Color(0xFF1C4A38)
    val Warn = Color(0xFFF2B84B)
    val Error = Color(0xFFFF6B6B)
}

@Composable
fun PhoneMicTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = Palette.Live,
            onPrimary = Color(0xFF00210F),
            background = Palette.Background,
            onBackground = Palette.Text,
            surface = Palette.Surface,
            onSurface = Palette.Text,
            surfaceVariant = Palette.SurfaceHigh,
            onSurfaceVariant = Palette.TextDim,
            outline = Palette.Outline,
            error = Palette.Error,
        ),
        content = content,
    )
}
