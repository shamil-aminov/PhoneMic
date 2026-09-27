package sh.aminov.phonemic.audio

import android.os.SystemClock
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import sh.aminov.phonemic.data.Pairing
import sh.aminov.phonemic.data.Prefs
import sh.aminov.phonemic.net.Packet
import sh.aminov.phonemic.net.PacketWriter
import sh.aminov.phonemic.net.Protocol
import sh.aminov.phonemic.net.TcpTransport
import sh.aminov.phonemic.net.Transport
import sh.aminov.phonemic.net.TransportKind
import sh.aminov.phonemic.net.UdpTransport
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import kotlin.math.abs
import kotlin.random.Random

sealed interface LinkState {
    data object Off : LinkState
    data class Searching(val pcName: String) : LinkState
    data class Connected(val pcName: String, val transport: TransportKind, val rttMs: Int?) : LinkState
    data class Problem(val refusal: Refusal) : LinkState
}

/** Why the PC turned the phone away. The UI puts it into words. */
enum class Refusal { WrongToken, Busy }

/** Live state shared by the service, its notification and the UI. */
object MicState {
    val link = MutableStateFlow<LinkState>(LinkState.Off)
    val level = MutableStateFlow(0f)

    /** True while the microphone cannot be opened, e.g. during a phone call. */
    val micUnavailable = MutableStateFlow(false)
}

private const val TAG = "MicEngine"
private const val TIMEOUT_MS = 3000L
private const val USB_PROBE_MS = 1500L

/** A cable that has answered this many probes in a row is trusted; a loose one would flap. */
private const val USB_STABLE_PROBES = 3

/**
 * Keeps two threads running until [stop]:
 *
 * * **capture** reads 10 ms frames from the microphone and sends each one to
 *   the PC if a link is up. Recording never pauses while reconnecting, so audio
 *   resumes the instant the link comes back.
 * * **link** finds the PC (USB first, then Wi-Fi), then pings it and watches
 *   for replies. Three silent seconds count as a lost link and it starts over.
 *   While on Wi-Fi it keeps an eye out for a USB cable and moves over to it
 *   once the cable has been there for a few seconds.
 */
class MicEngine(
    private val pairing: Pairing,
    private val prefs: Prefs,
    private val deviceName: String,
    private val allowUsb: Boolean = true,
    private val sourceFactory: () -> AudioSource,
) {
    @Volatile private var running = false
    @Volatile private var link: ActiveLink? = null
    private var captureThread: Thread? = null
    private var linkThread: Thread? = null

    private class ActiveLink(val transport: Transport, val session: Int) {
        @Volatile var seq = 0
    }

    fun start() {
        current = this
        running = true
        captureThread = Thread(::captureLoop, "PhoneMic capture").apply {
            priority = Thread.MAX_PRIORITY
            start()
        }
        linkThread = Thread(::linkLoop, "PhoneMic link").apply { start() }
    }

    fun stop() {
        running = false
        link?.let { l ->
            runCatching { l.transport.send(PacketWriter().begin(Protocol.BYE, l.session)) }
            l.transport.close()
        }
        captureThread?.join(1000)
        linkThread?.join(2000)
        if (current === this) {
            MicState.level.value = 0f
            MicState.micUnavailable.value = false
        }
    }

    private fun captureLoop() {
        android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_URGENT_AUDIO)
        val frame = ShortArray(FRAME_SAMPLES)
        val writer = PacketWriter()
        var meterCountdown = 0
        var meterPeak = 0
        while (running) {
            val source = try {
                sourceFactory()
            } catch (e: Exception) {
                Log.w(TAG, "Microphone unavailable", e)
                // Android refuses when another app holds the mic (a call) or when the service was
                // started while the phone was locked. Keep retrying: the first case clears up by itself.
                MicState.micUnavailable.value = true
                SystemClock.sleep(2000)
                continue
            }
            MicState.micUnavailable.value = false
            source.use {
                while (running) {
                    if (!source.read(frame)) {
                        Log.w(TAG, "Microphone read failed, reopening")
                        break
                    }
                    for (s in frame) meterPeak = maxOf(meterPeak, abs(s.toInt()))
                    if (--meterCountdown <= 0) {
                        MicState.level.value = meterPeak / 32768f
                        meterPeak = 0
                        meterCountdown = 3
                    }
                    val l = link ?: continue
                    try {
                        l.transport.send(writer.begin(Protocol.AUDIO, l.session).u32(l.seq++).samples(frame, FRAME_SAMPLES))
                    } catch (_: IOException) {
                        // The link thread notices and reconnects.
                    }
                }
            }
        }
    }

    private fun linkLoop() {
        var next: ActiveLink? = null
        while (running) {
            val active = next ?: run {
                MicState.link.value = LinkState.Searching(pairing.pcName)
                val session = Random.nextInt()
                val transport = try {
                    connect(session)
                } catch (e: Rejected) {
                    MicState.link.value = LinkState.Problem(e.refusal)
                    SystemClock.sleep(3000)
                    null
                }
                if (transport == null) {
                    SystemClock.sleep(500)
                    return@run null
                }
                ActiveLink(transport, session)
            } ?: continue
            next = null
            link = active
            try {
                next = watch(active)
                if (next != null) {
                    Log.i(TAG, "USB cable found, moving over from Wi-Fi")
                    // Hand the capture thread the new link before the old one goes away.
                    link = next
                }
            } catch (e: IOException) {
                Log.i(TAG, "Link lost: ${e.message}")
            } finally {
                if (link === active) link = null
                active.transport.close()
            }
        }
        next?.transport?.close()
        // A quick off/on leaves this thread finishing after the next engine started.
        if (current === this) MicState.link.value = LinkState.Off
    }

    /**
     * Pings until the PC stops answering (throws) or the engine stops (returns null).
     * On Wi-Fi, returns a ready USB link instead once a cable has stayed plugged in.
     */
    private fun watch(active: ActiveLink): ActiveLink? {
        val writer = PacketWriter()
        var lastRx = SystemClock.elapsedRealtime()
        var lastPing = 0L
        var lastUsbProbe = SystemClock.elapsedRealtime()
        var usbSeen = 0
        var rtt: Int? = null
        MicState.link.value = LinkState.Connected(pairing.pcName, active.transport.kind, null)
        while (running) {
            val now = SystemClock.elapsedRealtime()
            if (now - lastPing >= 500) {
                active.transport.send(writer.begin(Protocol.PING, active.session).u64(now))
                lastPing = now
            }
            if (allowUsb && active.transport.kind == TransportKind.WIFI && now - lastUsbProbe >= USB_PROBE_MS) {
                lastUsbProbe = now
                usbSeen = if (usbForwarded()) usbSeen + 1 else 0
                if (usbSeen >= USB_STABLE_PROBES) {
                    usbSeen = 0
                    val session = Random.nextInt()
                    val usb = try {
                        connectUsb(session)
                    } catch (_: Rejected) {
                        null
                    }
                    if (usb != null) return ActiveLink(usb, session)
                }
            }
            val p = active.transport.receive(100)
            if (p != null && p.session == active.session) {
                lastRx = SystemClock.elapsedRealtime()
                when (p.type) {
                    Protocol.PONG -> {
                        val sample = (lastRx - p.u64()).toInt()
                        rtt = rtt?.let { (it * 3 + sample) / 4 } ?: sample
                        MicState.link.value = LinkState.Connected(pairing.pcName, active.transport.kind, rtt)
                    }
                    Protocol.BYE -> throw IOException("PC said bye")
                }
            }
            if (SystemClock.elapsedRealtime() - lastRx > TIMEOUT_MS) throw IOException("timed out")
        }
        return null
    }

    /**
     * Cheap check for a cable: adb reverse is in place if something accepts on
     * 127.0.0.1. Whether the PC is really behind it is settled by the handshake.
     */
    private fun usbForwarded(): Boolean = try {
        java.net.Socket().use { it.connect(InetSocketAddress("127.0.0.1", pairing.port), 200) }
        true
    } catch (_: IOException) {
        false
    }

    private companion object {
        @Volatile var current: MicEngine? = null
    }

    private class Rejected(val refusal: Refusal) : Exception(refusal.name)

    private fun hello(writer: PacketWriter, session: Int) =
        writer.begin(Protocol.HELLO, session)
            .str(pairing.token).str(prefs.deviceId).str(deviceName).u32(SAMPLE_RATE).u8(1)

    private fun checkWelcome(p: Packet): Boolean {
        val status = p.u8()
        return when (status) {
            Protocol.STATUS_OK -> true
            Protocol.STATUS_BAD_TOKEN -> throw Rejected(Refusal.WrongToken)
            Protocol.STATUS_BUSY -> throw Rejected(Refusal.Busy)
            else -> false
        }
    }

    /** USB if adb reverse is set up, otherwise Wi-Fi. Null if the PC did not answer this round. */
    private fun connect(session: Int): Transport? = (if (allowUsb) connectUsb(session) else null) ?: connectWifi(session)

    private fun connectUsb(session: Int): Transport? {
        val tcp = try {
            TcpTransport.connect(pairing.port, 300)
        } catch (_: IOException) {
            return null
        }
        try {
            tcp.send(hello(PacketWriter(), session))
            val deadline = SystemClock.elapsedRealtime() + 1500
            while (SystemClock.elapsedRealtime() < deadline) {
                val p = tcp.receive(300) ?: continue
                if (p.type == Protocol.WELCOME && p.session == session && checkWelcome(p)) return tcp
            }
        } catch (_: IOException) {
            // adb accepted the connection but nothing is listening on the PC.
        } catch (e: Rejected) {
            tcp.close()
            throw e
        }
        tcp.close()
        return null
    }

    private fun connectWifi(session: Int): Transport? {
        val socket = DatagramSocket().apply { broadcast = true }
        try {
            val targets = (listOfNotNull(prefs.lastAddress) + pairing.addresses + "255.255.255.255")
                .distinct()
                .map { InetSocketAddress(it, pairing.port) }
            val writer = hello(PacketWriter(), session)
            for (target in targets) {
                runCatching { socket.send(DatagramPacket(writer.array, writer.length, target)) }
                    .onFailure { Log.d(TAG, "HELLO to $target failed: ${it.message}") }
            }
            val rx = ByteArray(Protocol.MAX_PACKET)
            val deadline = SystemClock.elapsedRealtime() + 1000
            while (running) {
                val left = deadline - SystemClock.elapsedRealtime()
                if (left <= 0) break
                socket.soTimeout = left.toInt()
                val datagram = DatagramPacket(rx, rx.size)
                try {
                    socket.receive(datagram)
                } catch (_: SocketTimeoutException) {
                    break
                }
                val p = Packet.parse(rx, datagram.length) ?: continue
                if (p.type != Protocol.WELCOME || p.session != session || !checkWelcome(p)) continue
                val from = datagram.socketAddress as InetSocketAddress
                prefs.lastAddress = from.address.hostAddress
                return UdpTransport(socket, from)
            }
        } catch (e: Rejected) {
            socket.close()
            throw e
        } catch (e: IOException) {
            Log.d(TAG, "Wi-Fi connect failed: ${e.message}")
        }
        socket.close()
        return null
    }
}
