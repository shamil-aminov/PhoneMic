package sh.aminov.phonemic.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.core.withInfiniteAnimationFrameNanos
import androidx.compose.foundation.Canvas
import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.BlendMode
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.exp
import kotlin.math.log10
import kotlin.math.sin

enum class WaveMode { Off, Waiting, Live }

private const val LINES = 34
private const val POINTS = 120

/** Seconds since the wave first appeared, advancing every frame. */
@Composable
fun rememberFrameTime(): State<Float> = produceState(0f) {
    val start = withInfiniteAnimationFrameNanos { it }
    while (true) withInfiniteAnimationFrameNanos { value = (it - start) / 1_000_000_000f }
}

/**
 * A glowing ribbon of thin lines that swells with the voice.
 *
 * The lines are views of one twisted ribbon: each line mixes two wave shapes,
 * `cos θ · f(x) + sin θ · g(x)`, with θ stepping across the bundle. Where
 * both shapes cross zero the lines pinch into a knot; elsewhere they fan out,
 * which is what gives the ribbon its depth. Everything sits under a bell-shaped
 * envelope, fades out towards the screen edges, and is drawn additively twice,
 * wide and faint for the glow, then thin and bright.
 * Off, the ribbon collapses into a dim thread; while connecting it breathes.
 *
 * @param level loudest recent sample, 0..1, straight from the microphone.
 * @param time seconds, normally [rememberFrameTime]; fixed in screenshot tests.
 */
@Composable
fun Wave(mode: WaveMode, level: Float, time: State<Float>, modifier: Modifier = Modifier) {
    // Loudness on a dB scale: speech sits in the upper half, room noise near zero.
    val loudness = if (mode == WaveMode.Live) ((20 * log10(level.coerceAtLeast(1e-4f)) + 50) / 44).coerceIn(0f, 1f) else 0f
    val energy by animateFloatAsState(loudness, tween(140), label = "energy")
    val presence by animateFloatAsState(
        when (mode) {
            WaveMode.Off -> 0f
            WaveMode.Waiting -> 0.5f
            WaveMode.Live -> 1f
        },
        tween(450), label = "presence",
    )
    val path = remember { Path() }
    val ys = remember { FloatArray((POINTS + 1) * 2) }

    Canvas(modifier) {
        val t = time.value
        val w = size.width
        val mid = size.height / 2
        val brush = Brush.horizontalGradient(
            0f to Palette.Cyan.copy(alpha = 0f),
            0.2f to Palette.Cyan,
            0.35f to Palette.Blue,
            0.5f to Palette.Violet,
            0.65f to Palette.Blue,
            0.8f to Palette.Cyan,
            1f to Palette.Cyan.copy(alpha = 0f),
            startX = 0f,
            endX = w,
        )
        // A little movement even in silence, so a live mic never looks dead.
        val breathing = 0.07f + 0.03f * sin(t * 1.3f)
        val swell = presence * (breathing + (1 - breathing) * energy)
        val amplitude = size.height * 0.46f * (0.012f + swell)
        val glow = Stroke(width = 5f * density)
        val line = Stroke(width = 1f * density)
        val bright = 0.35f + 0.65f * presence

        // The two shapes the ribbon is made of, sampled once per frame.
        val tau = 2 * PI.toFloat()
        for (j in 0..POINTS) {
            val x = j / POINTS.toFloat()
            val envelope = exp(-((x - 0.5f) / 0.25f).let { it * it })
            ys[j * 2] = envelope * (0.62f * sin(x * tau * 1.7f + t * 1.8f) + 0.38f * sin(x * tau * 3.3f - t * 2.5f))
            ys[j * 2 + 1] = envelope * (0.55f * sin(x * tau * 2.4f - t * 1.4f + 1.3f) + 0.45f * sin(x * tau * 4.6f + t * 3.1f))
        }

        for (i in 0 until LINES) {
            val theta = PI.toFloat() * i / LINES
            val c = kotlin.math.cos(theta)
            val sn = sin(theta)
            // Lines facing the viewer (θ near 0 or π) are brighter than the ones seen edge-on.
            val facing = 0.45f + 0.55f * abs(c)
            path.reset()
            for (j in 0..POINTS) {
                val py = mid + amplitude * (c * ys[j * 2] + sn * ys[j * 2 + 1])
                val px = j / POINTS.toFloat() * w
                if (j == 0) path.moveTo(px, py) else path.lineTo(px, py)
            }
            drawPath(path, brush, alpha = 0.035f * facing * bright, style = glow, blendMode = BlendMode.Plus)
            drawPath(path, brush, alpha = 0.32f * facing * bright, style = line, blendMode = BlendMode.Plus)
        }
    }
}
