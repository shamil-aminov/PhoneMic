package sh.aminov.phonemic

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch
import sh.aminov.phonemic.audio.LinkState
import sh.aminov.phonemic.audio.MicEngine
import sh.aminov.phonemic.audio.MicSource
import sh.aminov.phonemic.audio.MicState
import sh.aminov.phonemic.audio.ToneSource
import sh.aminov.phonemic.data.Prefs
import sh.aminov.phonemic.net.TransportKind

/**
 * Owns the microphone while it is switched on. Being a foreground service is
 * what keeps Android from muting or killing the app when the screen goes off;
 * the wake and Wi-Fi locks stop the radio from dozing between packets.
 */
class MicService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var engine: MicEngine? = null
    private var wakeLock: PowerManager.WakeLock? = null
    private val wifiLocks = mutableListOf<WifiManager.WifiLock>()

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }
        val prefs = Prefs(this)
        val pairing = prefs.pairing
        if (pairing == null || engine != null) {
            if (pairing == null) stopSelf()
            return START_NOT_STICKY
        }

        ServiceCompat.startForeground(
            this, NOTIFICATION_ID, buildNotification(MicState.link.value),
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE else 0,
        )
        acquireLocks()

        val tone = intent?.getBooleanExtra(EXTRA_TONE, false) == true
        val noiseSuppression = prefs.noiseSuppression
        val wifiOnly = intent?.getBooleanExtra(EXTRA_WIFI_ONLY, false) == true
        engine = MicEngine(pairing, prefs, Build.MODEL, allowUsb = !wifiOnly) {
            if (tone) ToneSource() else MicSource(noiseSuppression)
        }.also { it.start() }
        running.value = true

        scope.launch {
            combine(MicState.link, MicState.micError) { link, error -> link to error }.collect { (link, error) ->
                getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, buildNotification(link, error))
            }
        }
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        scope.cancel()
        val e = engine
        engine = null
        running.value = false
        // Joining the engine's threads can take a moment; keep it off the main thread.
        Thread { e?.stop() }.start()
        wakeLock?.release()
        wifiLocks.forEach { if (it.isHeld) it.release() }
        wifiLocks.clear()
        super.onDestroy()
    }

    private fun acquireLocks() {
        wakeLock = getSystemService(PowerManager::class.java)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "PhoneMic:stream")
            .apply { acquire() }
        // With the screen off, Wi-Fi power saving holds packets back for 100-300 ms.
        // LOW_LATENCY only applies while the screen is on; HIGH_PERF covers the
        // screen-off case up to Android 13 (later versions ignore it).
        val wifi = applicationContext.getSystemService(WifiManager::class.java)
        @Suppress("DEPRECATION")
        wifiLocks += wifi.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "PhoneMic:screen-off")
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            wifiLocks += wifi.createWifiLock(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, "PhoneMic:screen-on")
        }
        wifiLocks.forEach { it.acquire() }
    }

    private fun buildNotification(link: LinkState, micError: String? = null): Notification {
        val manager = getSystemService(NotificationManager::class.java)
        if (manager.getNotificationChannel(CHANNEL_ID) == null) {
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL_ID, getString(R.string.channel_name), NotificationManager.IMPORTANCE_LOW)
            )
        }
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE,
        )
        val stop = PendingIntent.getService(
            this, 1, Intent(this, MicService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE,
        )
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_mic)
            .setContentTitle(getString(R.string.notification_title))
            .setContentText(micError ?: describe(link))
            .setContentIntent(open)
            .setOngoing(true)
            .setSilent(true)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .addAction(0, getString(R.string.turn_off), stop)
            .build()
    }

    companion object {
        private const val CHANNEL_ID = "mic"
        private const val NOTIFICATION_ID = 1
        private const val ACTION_STOP = "sh.aminov.phonemic.STOP"
        const val EXTRA_TONE = "tone"
        const val EXTRA_WIFI_ONLY = "wifionly"

        /** True between start and stop, for the UI's on/off button. */
        val running = kotlinx.coroutines.flow.MutableStateFlow(false)

        fun start(context: Context, tone: Boolean = false, wifiOnly: Boolean = false) {
            ContextCompat.startForegroundService(
                context,
                Intent(context, MicService::class.java).putExtra(EXTRA_TONE, tone).putExtra(EXTRA_WIFI_ONLY, wifiOnly),
            )
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, MicService::class.java))
        }

        fun describe(link: LinkState): String = when (link) {
            LinkState.Off -> "Выключен"
            is LinkState.Searching -> "Ищу «${link.pcName}»…"
            is LinkState.Connected -> buildString {
                append("Передаю на «${link.pcName}» · ")
                append(if (link.transport == TransportKind.USB) "USB" else "Wi-Fi")
                link.rttMs?.let { append(" · $it мс") }
            }
            is LinkState.Problem -> link.message
        }
    }
}
