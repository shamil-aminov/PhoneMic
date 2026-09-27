package sh.aminov.phonemic.audio

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.SystemClock
import java.io.Closeable
import kotlin.math.PI
import kotlin.math.sin

const val SAMPLE_RATE = 48_000

/** 10 ms of audio, the size of one network packet. */
const val FRAME_SAMPLES = SAMPLE_RATE / 100

interface AudioSource : Closeable {
    /** Blocks until [frame] is filled with [FRAME_SAMPLES] samples. Returns false if the source failed. */
    fun read(frame: ShortArray): Boolean
}

/**
 * The phone's microphone. VOICE_COMMUNICATION turns on the platform's echo
 * cancellation and noise suppression; MIC is the plain signal, which sounds
 * more natural and lets the PC side apply its own processing.
 */
class MicSource(noiseSuppression: Boolean) : AudioSource {
    private val record: AudioRecord

    init {
        val source = if (noiseSuppression) MediaRecorder.AudioSource.VOICE_COMMUNICATION else MediaRecorder.AudioSource.MIC
        val min = AudioRecord.getMinBufferSize(SAMPLE_RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
        @SuppressLint("MissingPermission") // Checked before the service starts.
        val r = AudioRecord(source, SAMPLE_RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, maxOf(min, FRAME_SAMPLES * 2 * 8))
        check(r.state == AudioRecord.STATE_INITIALIZED) { "AudioRecord failed to initialize" }
        r.startRecording()
        record = r
    }

    override fun read(frame: ShortArray): Boolean {
        var filled = 0
        while (filled < FRAME_SAMPLES) {
            val n = record.read(frame, filled, FRAME_SAMPLES - filled)
            if (n <= 0) return false
            filled += n
        }
        return true
    }

    override fun close() {
        runCatching { record.stop() }
        record.release()
    }
}

/** A 1 kHz sine paced in real time. Lets the whole pipeline be tested without anyone speaking. */
class ToneSource(private val frequency: Double = 1000.0) : AudioSource {
    private var phase = 0.0
    private val start = SystemClock.elapsedRealtimeNanos()
    private var frames = 0L

    override fun read(frame: ShortArray): Boolean {
        val due = start + (frames + 1) * 10_000_000L
        val wait = (due - SystemClock.elapsedRealtimeNanos()) / 1_000_000
        if (wait > 0) Thread.sleep(wait)
        for (i in 0 until FRAME_SAMPLES) {
            frame[i] = (sin(phase) * 8000).toInt().toShort()
            phase += 2 * PI * frequency / SAMPLE_RATE
            if (phase > 2 * PI) phase -= 2 * PI
        }
        frames++
        return true
    }

    override fun close() {}
}
