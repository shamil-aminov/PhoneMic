package sh.aminov.phonemic.net

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * The same byte-exact vectors are checked by the PC side in
 * desktop/tests/PhoneMic.Core.Tests/ProtocolTests.cs and listed in
 * docs/protocol.md. If one side changes the wire format, both tests fail.
 */
class ProtocolTest {
    private fun PacketWriter.hex() = array.copyOf(length).joinToString("") { "%02X".format(it) }

    private fun bytes(hex: String) = ByteArray(hex.length / 2) { hex.substring(it * 2, it * 2 + 2).toInt(16).toByte() }

    @Test
    fun helloMatchesTheSpec() {
        val packet = PacketWriter().begin(Protocol.HELLO, 0x01020304)
            .str("ABC").str("d").str("N").u32(48000).u8(1)
        assertEquals(HELLO, packet.hex())
    }

    @Test
    fun audioMatchesTheSpec() {
        val packet = PacketWriter().begin(Protocol.AUDIO, 0x01020304)
            .u32(7).samples(shortArrayOf(1, -2), 2)
        assertEquals(AUDIO, packet.hex())
    }

    @Test
    fun welcomeFromThePcParses() {
        val data = bytes(WELCOME)
        val packet = Packet.parse(data, data.size)!!
        assertEquals(Protocol.WELCOME, packet.type)
        assertEquals(0x01020304, packet.session)
        assertEquals(Protocol.STATUS_OK, packet.u8())
        assertEquals("PC", packet.str())
    }

    @Test
    fun foreignDataIsIgnored() {
        val data = "GET / HTTP/1.1".toByteArray()
        assertEquals(null, Packet.parse(data, data.size))
    }

    private companion object {
        const val HELLO = "504D010104030201" + "03414243" + "0164" + "014E" + "80BB0000" + "01"
        const val AUDIO = "504D010304030201" + "07000000" + "0100" + "FEFF"
        const val WELCOME = "504D010204030201" + "00" + "025043"
    }
}
