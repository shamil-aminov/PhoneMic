using System.Buffers.Binary;
using System.Text;

namespace PhoneMic.Core;

/// <summary>Wire format shared with the Android app. See docs/protocol.md.</summary>
public static class Protocol
{
    public const byte Version = 1;
    public const int HeaderSize = 8;
    public const int DefaultPort = 50505;
    public const int MaxPacketSize = 4096;

    public const byte Hello = 1;
    public const byte Welcome = 2;
    public const byte Audio = 3;
    public const byte Ping = 4;
    public const byte Pong = 5;
    public const byte Bye = 6;

    public const byte StatusOk = 0;
    public const byte StatusBadToken = 1;
    public const byte StatusBusy = 2;
}

public readonly record struct PacketHeader(byte Type, uint Session);

public sealed record HelloPayload(string Token, string DeviceId, string DeviceName, int SampleRate, int Channels);

/// <summary>Builds one packet into a reusable buffer.</summary>
public sealed class PacketWriter
{
    private readonly byte[] _buffer = new byte[Protocol.MaxPacketSize];
    private int _length;

    public PacketWriter Begin(byte type, uint session)
    {
        _buffer[0] = (byte)'P';
        _buffer[1] = (byte)'M';
        _buffer[2] = Protocol.Version;
        _buffer[3] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(4), session);
        _length = Protocol.HeaderSize;
        return this;
    }

    public PacketWriter U8(byte value)
    {
        _buffer[_length++] = value;
        return this;
    }

    public PacketWriter U32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_length), value);
        _length += 4;
        return this;
    }

    public PacketWriter U64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_length), value);
        _length += 8;
        return this;
    }

    public PacketWriter Str(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var count = Math.Min(bytes.Length, 255);
        _buffer[_length++] = (byte)count;
        bytes.AsSpan(0, count).CopyTo(_buffer.AsSpan(_length));
        _length += count;
        return this;
    }

    public PacketWriter Samples(ReadOnlySpan<short> samples)
    {
        foreach (var s in samples)
        {
            BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_length), s);
            _length += 2;
        }
        return this;
    }

    public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);
    public ReadOnlyMemory<byte> Memory => _buffer.AsMemory(0, _length);
}

/// <summary>Reads fields from a received packet. Throws <see cref="FormatException"/> on truncated input.</summary>
public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public PacketReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = Protocol.HeaderSize;
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> data, out PacketHeader header)
    {
        header = default;
        if (data.Length < Protocol.HeaderSize || data[0] != 'P' || data[1] != 'M' || data[2] != Protocol.Version)
            return false;
        header = new PacketHeader(data[3], BinaryPrimitives.ReadUInt32LittleEndian(data[4..]));
        return true;
    }

    public int Remaining => _data.Length - _pos;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > Remaining) throw new FormatException("Truncated packet");
        var span = _data.Slice(_pos, count);
        _pos += count;
        return span;
    }

    public byte U8() => Take(1)[0];
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public string Str() => Encoding.UTF8.GetString(Take(U8()));

    /// <summary>The rest of the packet as 16-bit samples.</summary>
    public ReadOnlySpan<byte> RestAsSampleBytes() => Take(Remaining & ~1);

    public HelloPayload Hello() => new(Str(), Str(), Str(), (int)U32(), U8());
}
