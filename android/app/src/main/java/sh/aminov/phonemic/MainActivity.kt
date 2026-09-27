package sh.aminov.phonemic

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import sh.aminov.phonemic.audio.MicState
import sh.aminov.phonemic.data.Pairing
import sh.aminov.phonemic.data.Prefs
import sh.aminov.phonemic.ui.MainScreen
import sh.aminov.phonemic.ui.PhoneMicTheme
import sh.aminov.phonemic.ui.ScreenState

class MainActivity : ComponentActivity() {
    private lateinit var prefs: Prefs
    private var pairing by mutableStateOf<Pairing?>(null)
    private var noiseSuppression by mutableStateOf(false)

    /** What to do once permissions are granted. */
    private var pendingStart: (() -> Unit)? = null

    private val permissions = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        if (result[Manifest.permission.RECORD_AUDIO] == true) {
            pendingStart?.invoke()
        } else {
            Toast.makeText(this, R.string.mic_permission_needed, Toast.LENGTH_LONG).show()
        }
        pendingStart = null
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // The app is always dark, whatever the system theme: light icons, and no
        // light scrim behind three-button navigation breaking the black screen.
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
        )
        prefs = Prefs(this)
        pairing = prefs.pairing
        noiseSuppression = prefs.noiseSuppression

        setContent {
            val running by MicService.running.collectAsStateWithLifecycle()
            val link by MicState.link.collectAsStateWithLifecycle()
            val level by MicState.level.collectAsStateWithLifecycle()
            val micError by MicState.micError.collectAsStateWithLifecycle()
            PhoneMicTheme {
                MainScreen(
                    state = ScreenState(
                        running = running,
                        link = link,
                        level = level,
                        micError = micError,
                        pairing = pairing,
                        noiseSuppression = noiseSuppression,
                    ),
                    onToggle = { if (running) MicService.stop(this) else startMic() },
                    onScan = { scan() },
                    onNoiseSuppressionChange = ::changeNoiseSuppression,
                    version = BuildConfig.VERSION_NAME,
                )
            }
        }
        handleIntent(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    /**
     * Accepts a pairing link (a QR code opened by the camera app lands here) and
     * the debug extras used for automated testing over adb:
     * `--ez autostart true`, `--ez tone true`, `--ez wifionly true`, `--ez stop true`.
     */
    private fun handleIntent(intent: Intent) {
        intent.dataString?.let { applyPairing(it) }
        when {
            intent.getBooleanExtra("stop", false) -> MicService.stop(this)
            intent.getBooleanExtra("autostart", false) -> startMic(
                tone = intent.getBooleanExtra("tone", false),
                wifiOnly = intent.getBooleanExtra("wifionly", false),
            )
        }
    }

    private fun applyPairing(text: String): Boolean {
        val parsed = Pairing.parse(text)
        if (parsed == null) {
            Toast.makeText(this, R.string.not_a_phonemic_code, Toast.LENGTH_LONG).show()
            return false
        }
        val wasRunning = MicService.running.value
        prefs.pairing = parsed
        prefs.lastAddress = null
        pairing = parsed
        if (wasRunning) {
            MicService.stop(this)
            window.decorView.postDelayed({ startMic() }, 300)
        }
        Toast.makeText(this, getString(R.string.paired_with, parsed.pcName), Toast.LENGTH_SHORT).show()
        return true
    }

    private fun scan(then: (() -> Unit)? = null) {
        val options = GmsBarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build()
        GmsBarcodeScanning.getClient(this, options).startScan()
            .addOnSuccessListener { code -> if (applyPairing(code.rawValue.orEmpty())) then?.invoke() }
            .addOnFailureListener { Toast.makeText(this, R.string.scanner_failed, Toast.LENGTH_LONG).show() }
    }

    private fun startMic(tone: Boolean = false, wifiOnly: Boolean = false) {
        if (prefs.pairing == null) {
            scan { startMic(tone, wifiOnly) }
            return
        }
        val needed = buildList {
            add(Manifest.permission.RECORD_AUDIO)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) add(Manifest.permission.POST_NOTIFICATIONS)
        }.filter { ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED }
        if (needed.isEmpty()) {
            MicService.start(this, tone, wifiOnly)
        } else {
            pendingStart = { MicService.start(this, tone, wifiOnly) }
            permissions.launch(needed.toTypedArray())
        }
    }

    private fun changeNoiseSuppression(enabled: Boolean) {
        prefs.noiseSuppression = enabled
        noiseSuppression = enabled
        if (MicService.running.value) {
            MicService.stop(this)
            window.decorView.postDelayed({ startMic() }, 300)
        }
    }
}
