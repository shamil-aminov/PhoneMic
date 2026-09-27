using PhoneMic.Core;

namespace PhoneMic.Core.Tests;

public class ProtocolTests
{
    // The same byte-exact vectors are checked by the phone side in
    // android/app/src/test/.../ProtocolTest.kt and listed in docs/protocol.md.
    private const string HelloVector = "504D010104030201" + "03414243" + "0164" + "014E" + "80BB0000" + "01";
    private const string AudioVector = "504D010304030201" + "07000000" + "0100" + "FEFF";
    private const string WelcomeVector = "504D010204030201" + "00" + "025043";

    [Fact]
    public void Hello_from_the_phone_parses()
    {
        var data = Convert.FromHexString(HelloVector);
        Assert.True(PacketReader.TryReadHeader(data, out var header));
        Assert.Equal(Protocol.Hello, header.Type);
        Assert.Equal(0x01020304u, header.Session);
        Assert.Equal(new HelloPayload("ABC", "d", "N", 48000, 1), new PacketReader(data).Hello());
    }

    [Fact]
    public void Audio_from_the_phone_parses()
    {
        var data = Convert.FromHexString(AudioVector);
        var reader = new PacketReader(data);
        Assert.Equal(7u, reader.U32());
        Assert.Equal(new byte[] { 0x01, 0x00, 0xFE, 0xFF }, reader.RestAsSampleBytes().ToArray());
    }

    [Fact]
    public void Welcome_matches_the_spec()
    {
        var w = new PacketWriter().Begin(Protocol.Welcome, 0x01020304).U8(Protocol.StatusOk).Str("PC");
        Assert.Equal(WelcomeVector, Convert.ToHexString(w.Span));
    }

    [Fact]
    public void Writer_produces_the_phone_vectors()
    {
        var hello = new PacketWriter().Begin(Protocol.Hello, 0x01020304).Str("ABC").Str("d").Str("N").U32(48000).U8(1);
        Assert.Equal(HelloVector, Convert.ToHexString(hello.Span));
        var audio = new PacketWriter().Begin(Protocol.Audio, 0x01020304).U32(7).Samples(new short[] { 1, -2 });
        Assert.Equal(AudioVector, Convert.ToHexString(audio.Span));
    }

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
