package sh.aminov.phonemic.ui

import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onRoot
import com.github.takahirom.roborazzi.captureRoboImage
import com.github.takahirom.roborazzi.captureScreenRoboImage
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import sh.aminov.phonemic.audio.LinkState
import sh.aminov.phonemic.data.Pairing
import sh.aminov.phonemic.net.TransportKind

/**
 * Renders every state of the main screen on the JVM.
 *
 * `./gradlew recordRoborazziDebug` writes the images to app/screenshots/; a
 * plain `./gradlew test` renders them without saving, which still catches a
 * screen that crashes. The wave is drawn at a fixed moment so images only
 * change when the design does.
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [36], qualifiers = "w393dp-h852dp-xxhdpi")
class ScreenshotTest {
    @get:Rule
    val compose = createComposeRule()

    private val pc = Pairing(listOf("192.168.1.20"), 50505, "EXAMPLE7", "Studio PC")

    private fun state(
        running: Boolean = true,
        link: LinkState = LinkState.Connected("Studio PC", TransportKind.WIFI, 12),
        level: Float = 0.35f,
        micError: String? = null,
        pairing: Pairing? = pc,
    ) = ScreenState(running, link, level, micError, pairing, noiseSuppression = false)

    private fun shot(name: String, state: ScreenState, settings: Boolean = false) {
        compose.setContent {
            PhoneMicTheme {
                MainScreen(
                    state, onToggle = {}, onScan = {}, onNoiseSuppressionChange = {}, version = "1.0.0",
                    time = remember { mutableStateOf(1.35f) }, settingsInitiallyOpen = settings,
                )
            }
        }
        compose.mainClock.advanceTimeBy(1_000)
        if (settings) captureScreenRoboImage("screenshots/$name.png") else compose.onRoot().captureRoboImage("screenshots/$name.png")
    }

    @Test fun live() = shot("1-live", state())

    @Test fun liveUsbQuiet() = shot("2-live-usb-quiet", state(link = LinkState.Connected("Studio PC", TransportKind.USB, 3), level = 0.003f))

    @Test fun off() = shot("3-off", state(running = false, link = LinkState.Off, level = 0f))

    @Test fun connecting() = shot("4-connecting", state(link = LinkState.Searching("Studio PC"), level = 0.02f))

    @Test fun problem() = shot("5-problem", state(link = LinkState.Problem("К компьютеру уже подключён другой телефон")))

    @Test fun micDenied() = shot(
        "6-mic-denied",
        state(micError = "Не удалось открыть микрофон. Разблокируйте телефон и включите микрофон заново."),
    )

    @Test fun unpaired() = shot("7-unpaired", state(running = false, link = LinkState.Off, pairing = null))

    @Test fun settings() = shot("8-settings", state(running = false, link = LinkState.Off), settings = true)
}
