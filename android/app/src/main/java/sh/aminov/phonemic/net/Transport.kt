package sh.aminov.phonemic.net

import java.io.Closeable
import java.io.DataInputStream
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketAddress
import java.net.SocketTimeoutException

enum class TransportKind { WIFI, USB }

/** A connection to the PC that moves whole packets. Send and receive may be called from different threads. */
interface Transport : Closeable {
    val kind: TransportKind

    fun send(packet: PacketWriter)

    /** Blocks up to [timeoutMs]; returns null on timeout. Throws [IOException] once the link is dead. */
    fun receive(timeoutMs: Int): Packet?
}

/** Wi-Fi: one datagram per packet, locked to the address that answered our HELLO. */
class UdpTransport(private val socket: DatagramSocket, private val remote: SocketAddress) : Transport {
    override val kind = TransportKind.WIFI
    private val rx = ByteArray(Protocol.MAX_PACKET)

    override fun send(packet: PacketWriter) {
        socket.send(DatagramPacket(packet.array, packet.length, remote))
    }

    override fun receive(timeoutMs: Int): Packet? {
        socket.soTimeout = timeoutMs
        val datagram = DatagramPacket(rx, rx.size)
        return try {
            socket.receive(datagram)
            if (datagram.socketAddress != remote) null else Packet.parse(rx, datagram.length)
        } catch (_: SocketTimeoutException) {
            null
        }
    }

    override fun close() = socket.close()
}

/** USB: TCP to 127.0.0.1, which adb reverse forwards to the PC. Packets are length prefixed. */
class TcpTransport(private val socket: Socket) : Transport {
    override val kind = TransportKind.USB
    private val input = DataInputStream(socket.getInputStream().buffered())
    private val output = socket.getOutputStream()
    private val frame = ByteArray(Protocol.MAX_PACKET + 2)
    private val rx = ByteArray(Protocol.MAX_PACKET)

    override fun send(packet: PacketWriter) {
        synchronized(frame) {
            val n = packet.length
            frame[0] = (n and 0xFF).toByte()
            frame[1] = (n shr 8).toByte()
            System.arraycopy(packet.array, 0, frame, 2, n)
            output.write(frame, 0, n + 2)
        }
    }

    override fun receive(timeoutMs: Int): Packet? {
        socket.soTimeout = timeoutMs
        val lo = try {
            input.read()
        } catch (_: SocketTimeoutException) {
            return null
        }
        if (lo < 0) throw IOException("closed")
        // Once a frame has started, a stall means the stream is broken, not idle.
        socket.soTimeout = 2000
        try {
            val n = lo or (input.readUnsignedByte() shl 8)
            if (n > rx.size) throw IOException("frame too large")
            input.readFully(rx, 0, n)
            return Packet.parse(rx, n)
        } catch (e: SocketTimeoutException) {
            throw IOException("stalled mid-frame", e)
        }
    }

    override fun close() = socket.close()

    companion object {
        fun connect(port: Int, timeoutMs: Int): TcpTransport {
            val socket = Socket()
            try {
                socket.tcpNoDelay = true
                socket.connect(InetSocketAddress("127.0.0.1", port), timeoutMs)
            } catch (e: IOException) {
                socket.close()
                throw e
            }
            return TcpTransport(socket)
        }
    }
}
