package sh.aminov.phonemic.net

import java.nio.ByteBuffer
import java.nio.ByteOrder

/** Wire format shared with the PC app. See docs/protocol.md. */
object Protocol {
    const val VERSION: Byte = 1
    const val HEADER_SIZE = 8
    const val DEFAULT_PORT = 50505
    const val MAX_PACKET = 4096

    const val HELLO: Byte = 1
    const val WELCOME: Byte = 2
    const val AUDIO: Byte = 3
    const val PING: Byte = 4
    const val PONG: Byte = 5
    const val BYE: Byte = 6

    const val STATUS_OK = 0
    const val STATUS_BAD_TOKEN = 1
    const val STATUS_BUSY = 2
}

/** Builds one packet into a reusable buffer. Not thread safe; use one per thread. */
class PacketWriter {
    private val buffer = ByteBuffer.allocate(Protocol.MAX_PACKET).order(ByteOrder.LITTLE_ENDIAN)

    val array: ByteArray get() = buffer.array()
    val length: Int get() = buffer.position()

    fun begin(type: Byte, session: Int): PacketWriter {
        buffer.clear()
        buffer.put('P'.code.toByte()).put('M'.code.toByte()).put(Protocol.VERSION).put(type).putInt(session)
        return this
    }

    fun u8(value: Int) = apply { buffer.put(value.toByte()) }
    fun u32(value: Int) = apply { buffer.putInt(value) }
    fun u64(value: Long) = apply { buffer.putLong(value) }

    fun str(value: String) = apply {
        val bytes = value.toByteArray(Charsets.UTF_8).let { if (it.size > 255) it.copyOf(255) else it }
        buffer.put(bytes.size.toByte()).put(bytes)
    }

    fun samples(data: ShortArray, count: Int) = apply {
        for (i in 0 until count) buffer.putShort(data[i])
    }
}

/** A received packet. Field reads throw [java.nio.BufferUnderflowException] on truncated input. */
class Packet private constructor(val type: Byte, val session: Int, private val body: ByteBuffer) {
    fun u8(): Int = body.get().toInt() and 0xFF
    fun u64(): Long = body.long
    fun str(): String {
        val bytes = ByteArray(u8())
        body.get(bytes)
        return String(bytes, Charsets.UTF_8)
    }

    companion object {
        fun parse(data: ByteArray, length: Int): Packet? {
            if (length < Protocol.HEADER_SIZE) return null
            if (data[0] != 'P'.code.toByte() || data[1] != 'M'.code.toByte() || data[2] != Protocol.VERSION) return null
            val buffer = ByteBuffer.wrap(data, 0, length).order(ByteOrder.LITTLE_ENDIAN)
            buffer.position(4)
            val session = buffer.int
            return Packet(data[3], session, buffer.slice().order(ByteOrder.LITTLE_ENDIAN))
        }
    }
}
