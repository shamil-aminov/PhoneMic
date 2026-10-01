package sh.aminov.phonemic.ui

import android.graphics.Bitmap
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onRoot
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import sh.aminov.phonemic.audio.LinkState
import sh.aminov.phonemic.data.Pairing
import sh.aminov.phonemic.net.TransportKind
import java.io.File
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.pow
import kotlin.math.sin

/**
 * Frames of the phone screen for the README animation, one loop of
 * [demoLevel] at 15 fps into app/build/demo/<lang>/. Off unless PHONEMIC_DEMO
 * is set, since it renders a few hundred frames:
 *
 *     PHONEMIC_DEMO=1 ./gradlew testDebugUnitTest --tests "*DemoAnimation*"
 */
abstract class DemoAnimationTest(private val language: String) {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun frames() {
        assumeTrue(System.getenv("PHONEMIC_DEMO") != null)
        val pc = Pairing(listOf("192.168.1.20"), 50505, "EXAMPLE7", "Studio PC")
        val out = File("build/demo/$language").apply { deleteRecursively(); mkdirs() }
        compose.mainClock.autoAdvance = false
        lateinit var level: androidx.compose.runtime.MutableFloatState
        lateinit var time: androidx.compose.runtime.MutableFloatState
        compose.setContent {
            level = remember { mutableFloatStateOf(0f) }
            time = remember { mutableFloatStateOf(0f) }
            PhoneMicTheme {
                MainScreen(
                    ScreenState(true, LinkState.Connected("Studio PC", TransportKind.USB, 3), level.floatValue, false, pc, false),
                    onToggle = {}, onScan = {}, onNoiseSuppressionChange = {}, version = "1.0.1", time = time,
                )
            }
        }
        val frames = (FPS * PERIOD).toInt()
        // One loop unrecorded first, so the smoothed loudness is already where the loop leaves it.
        for (k in -frames until frames) {
            val t = (k.mod(frames)) / FPS.toFloat()
            level.floatValue = demoLevel(t.toDouble()).toFloat()
            time.floatValue = t
            compose.mainClock.advanceTimeBy(1000L / FPS)
            if (k < 0) continue
            val bitmap = compose.onRoot().captureToImage().asAndroidBitmap()
            File(out, "%03d.png".format(k)).outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        }
    }

    companion object {
        const val FPS = 15
        const val PERIOD = 6.0

        /** The same made-up speech as desktop/src/PhoneMic/DemoVoice.cs. */
        fun demoLevel(t: Double): Double {
            val phrase = phrase(t, 0.5, 2.5) + phrase(t, 3.1, 5.4)
            val syllable = abs(sin(PI * 4.3 * t)).pow(1.3)
            return 0.003 + 0.5 * phrase * syllable
        }

        private fun phrase(t: Double, from: Double, to: Double) = ease((t - from) / 0.12) * ease((to - t) / 0.12)

        private fun ease(x: Double): Double {
            val c = x.coerceIn(0.0, 1.0)
            return c * c * (3 - 2 * c)
        }
    }
}

@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [36], qualifiers = "en-w393dp-h852dp-xxhdpi")
class DemoAnimationEnglish : DemoAnimationTest("en")

@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [36], qualifiers = "ru-w393dp-h852dp-xxhdpi")
class DemoAnimationRussian : DemoAnimationTest("ru")
