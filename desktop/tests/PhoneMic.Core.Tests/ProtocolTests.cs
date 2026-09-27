using PhoneMic.Core;

namespace PhoneMic.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public void Hello_round_trips()
    {
        var w = new PacketWriter().Begin(Protocol.Hello, 0xDEADBEEF)
            .Str("TOKEN").Str("device-id").Str("Телефон").U32(48000).U8(1);

        Assert.True(PacketReader.TryReadHeader(w.Span, out var header));
        Assert.Equal(Protocol.Hello, header.Type);
        Assert.Equal(0xDEADBEEF, header.Session);

        var hello = new PacketReader(w.Span).Hello();
        Assert.Equal(new HelloPayload("TOKEN", "device-id", "Телефон", 48000, 1), hello);
    }

    [Fact]
    public void Garbage_and_truncated_packets_are_rejected()
    {
        Assert.False(PacketReader.TryReadHeader("GET / HTTP/1.1"u8, out _));
        Assert.False(PacketReader.TryReadHeader([(byte)'P', (byte)'M', 1], out _));

        var w = new PacketWriter().Begin(Protocol.Hello, 1).Str("TOKEN");
        Assert.Throws<FormatException>(() => new PacketReader(w.Span).Hello());
    }

    [Fact]
    public void Pairing_uri_lists_every_address()
    {
        var uri = Pairing.Uri([System.Net.IPAddress.Parse("192.168.1.2"), System.Net.IPAddress.Parse("10.0.0.3")], 50505, "ABC", "My PC");
        Assert.Equal("phonemic://pair?a=192.168.1.2,10.0.0.3&p=50505&t=ABC&n=My%20PC", uri);
    }
}
