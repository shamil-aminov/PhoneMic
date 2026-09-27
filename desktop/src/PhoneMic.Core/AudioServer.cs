using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace PhoneMic.Core;

public enum Transport
{
    WiFi,
    Usb,
}

/// <summary>What the UI shows about the phone that is currently streaming.</summary>
public sealed record SessionInfo(string DeviceName, Transport Transport, IPEndPoint Remote, int SampleRate);

/// <summary>
/// Accepts phones over UDP (Wi-Fi) and TCP (USB via adb reverse) on the same
/// port and feeds the audio of the one active phone into a <see cref="JitterBuffer"/>.
/// </summary>
public sealed class AudioServer : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly int _port;
    private readonly Func<string> _token;
    private readonly string _pcName;
    private readonly JitterBuffer _buffer;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly PacketWriter _udpWriter = new();
    private readonly JitterEstimator _jitter = new();

    private Socket? _udp;
    private TcpListener? _tcp;
    private Thread? _udpThread;
    private Timer? _watchdog;
    private Session? _session;
    private float _peak;

    public AudioServer(JitterBuffer buffer, Func<string> token, int port = Protocol.DefaultPort, string? pcName = null)
    {
        _buffer = buffer;
        _token = token;
        _port = port;
        _pcName = pcName ?? Environment.MachineName;
    }

    /// <summary>Raised on a background thread whenever a phone connects or disconnects.</summary>
    public event Action<SessionInfo?>? SessionChanged;

    public int Port => _port;

    public SessionInfo? Current
    {
        get { lock (_lock) return _session?.Info; }
    }

    public long PacketsReceived { get; private set; }
    public long PacketsLost { get; private set; }

    /// <summary>Loudest sample since the last call, after gain, 0..1.</summary>
    public float TakePeak()
    {
        lock (_lock)
        {
            var p = _peak;
            _peak = 0;
            return p;
        }
    }

    public void Start()
    {
        _udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // Without this, an ICMP "port unreachable" from a phone that went away
        // makes the next ReceiveFrom throw on Windows.
        const int SioUdpConnReset = -1744830452;
        _udp.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        _udp.EnableBroadcast = true;
        _udp.ReceiveBufferSize = 1 << 20;
        _udp.Bind(new IPEndPoint(IPAddress.Any, _port));
        _udpThread = new Thread(UdpLoop) { IsBackground = true, Name = "PhoneMic UDP", Priority = ThreadPriority.AboveNormal };
        _udpThread.Start();

        _tcp = new TcpListener(IPAddress.Any, _port);
        _tcp.Start();
        _ = AcceptLoop();

        _watchdog = new Timer(_ => CheckTimeout(), null, 500, 500);
        Log.Info($"Server listening on UDP and TCP port {_port}");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _watchdog?.Dispose();
        lock (_lock)
        {
            if (_session != null) Send(_session, new PacketWriter().Begin(Protocol.Bye, _session.Id));
            _session?.Tcp?.Dispose();
            _session = null;
        }
        _udp?.Dispose();
        _tcp?.Stop();
    }

    private void UdpLoop()
    {
        var buffer = new byte[Protocol.MaxPacketSize];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (!_cts.IsCancellationRequested)
        {
            int length;
            try
            {
                length = _udp!.ReceiveFrom(buffer, ref from);
            }
            catch (SocketException e)
            {
                if (_cts.IsCancellationRequested) return;
                Log.Error("UDP receive", e);
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            Handle(buffer.AsSpan(0, length), (IPEndPoint)from, null);
        }
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _tcp!.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException e)
            {
                Log.Error("TCP accept", e);
                continue;
            }
            client.NoDelay = true;
            _ = Task.Run(() => TcpLoop(client));
        }
    }

    private async Task TcpLoop(TcpClient client)
    {
        var remote = (IPEndPoint)client.Client.RemoteEndPoint!;
        var connection = new TcpConnection(client);
        var lengthBytes = new byte[2];
        var packet = new byte[Protocol.MaxPacketSize];
        try
        {
            var stream = client.GetStream();
            while (!_cts.IsCancellationRequested)
            {
                await stream.ReadExactlyAsync(lengthBytes, _cts.Token);
                var length = BinaryPrimitives.ReadUInt16LittleEndian(lengthBytes);
                if (length > packet.Length) break;
                await stream.ReadExactlyAsync(packet.AsMemory(0, length), _cts.Token);
                Handle(packet.AsSpan(0, length), remote, connection);
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or OperationCanceledException or SocketException)
        {
        }
        finally
        {
            lock (_lock)
            {
                if (_session?.Tcp == connection) EndSession("USB connection closed");
            }
            connection.Dispose();
        }
    }

    private void Handle(ReadOnlySpan<byte> data, IPEndPoint from, TcpConnection? tcp)
    {
        if (!PacketReader.TryReadHeader(data, out var header)) return;
        try
        {
            var reader = new PacketReader(data);
            lock (_lock)
            {
                switch (header.Type)
                {
                    case Protocol.Hello:
                        OnHello(header.Session, reader.Hello(), from, tcp);
                        break;
                    case Protocol.Audio:
                        if (_session?.Id != header.Session) return;
                        _session.Touch(from, tcp);
                        OnAudio(reader.U32(), reader.RestAsSampleBytes());
                        break;
                    case Protocol.Ping:
                        var stamp = reader.U64();
                        if (_session?.Id == header.Session)
                        {
                            _session.Touch(from, tcp);
                            Send(_session, _udpWriter.Begin(Protocol.Pong, header.Session).U64(stamp));
                        }
                        break;
                    case Protocol.Bye:
                        if (_session?.Id == header.Session) EndSession("phone said bye");
                        break;
                }
            }
        }
        catch (FormatException)
        {
        }
    }

    private void OnHello(uint id, HelloPayload hello, IPEndPoint from, TcpConnection? tcp)
    {
        var reply = new Session(id, hello, from, tcp);

        if (hello.Token != _token())
        {
            Log.Info($"Rejected {hello.DeviceName} at {from}: wrong pairing token");
            Send(reply, _udpWriter.Begin(Protocol.Welcome, id).U8(Protocol.StatusBadToken).Str(_pcName));
            return;
        }

        if (_session != null && _session.Id == id)
        {
            // Our WELCOME got lost, or the phone switched transport mid-session.
            _session.Touch(from, tcp);
            Send(_session, _udpWriter.Begin(Protocol.Welcome, id).U8(Protocol.StatusOk).Str(_pcName));
            return;
        }

        if (_session != null && _session.DeviceId != hello.DeviceId && !_session.IsExpired(Timeout))
        {
            Send(reply, _udpWriter.Begin(Protocol.Welcome, id).U8(Protocol.StatusBusy).Str(_pcName));
            return;
        }

        if (_session?.Tcp != null && _session.Tcp != tcp) _session.Tcp.Dispose();
        _session = reply;
        // Wi-Fi needs a cushion from the first second; USB is steady right away.
        _buffer.Reset(hello.SampleRate, headroomMs: tcp != null ? 10 : 40);
        _loggedUnderruns = 0;
        _jitter.Reset();
        _lastStats = Environment.TickCount64;
        PacketsReceived = 0;
        PacketsLost = 0;
        Send(_session, _udpWriter.Begin(Protocol.Welcome, id).U8(Protocol.StatusOk).Str(_pcName));
        Log.Info($"Connected {hello.DeviceName} via {reply.Info.Transport} from {from}, {hello.SampleRate} Hz");
        RaiseChanged(_session.Info);
    }

    private void OnAudio(uint seq, ReadOnlySpan<byte> pcm)
    {
        var s = _session!;
        var samples = pcm.Length / 2;
        var profile = _buffer.Profile;
        _jitter.OnPacket(seq * (samples * 1000.0 / s.SampleRate), profile.MaxJitterMs, profile.MemorySeconds);
        _buffer.JitterMs = _jitter.PeakMs;
        if (s.NextSeq is { } expected)
        {
            var gap = (long)seq - expected;
            if (gap < 0) return; // Late or duplicated packet; its slot was already filled.
            if (gap > 0)
            {
                PacketsLost += gap;
                if (gap <= 50) _buffer.WriteSilence((int)gap * samples);
            }
        }
        s.NextSeq = seq + 1;
        PacketsReceived++;
        _buffer.WritePcm16(pcm);

        var peak = 0;
        for (var i = 0; i + 1 < pcm.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm[i..])));
        _peak = Math.Max(_peak, Math.Min(1f, peak / 32768f * _buffer.Gain));
    }

    private int _loggedUnderruns;
    private long _lastStats;

    private void CheckTimeout()
    {
        lock (_lock)
        {
            if (_session != null && _session.IsExpired(Timeout)) EndSession("timed out");
            if (_session != null && _buffer.Underruns != _loggedUnderruns)
            {
                _loggedUnderruns = _buffer.Underruns;
                Log.Info($"Underrun #{_loggedUnderruns}, target now {_buffer.TargetMs:F0} ms, lost so far {PacketsLost}");
            }
            if (_session != null && Environment.TickCount64 - _lastStats >= 10_000)
            {
                _lastStats = Environment.TickCount64;
                Log.Info($"{_session.Info.Transport}: {_jitter.TakeStats()}, buffer {_buffer.LevelMs:F0}/{_buffer.TargetMs:F0} ms");
            }
        }
    }

    private void EndSession(string reason)
    {
        if (_session == null) return;
        Log.Info($"Disconnected {_session.Info.DeviceName}: {reason}. Received {PacketsReceived}, lost {PacketsLost}, underruns {_buffer.Underruns}");
        _session = null;
        _buffer.Reset();
        RaiseChanged(null);
    }

    private void RaiseChanged(SessionInfo? info) =>
        ThreadPool.QueueUserWorkItem(_ => SessionChanged?.Invoke(info));

    private void Send(Session session, PacketWriter packet)
    {
        try
        {
            if (session.Tcp != null) session.Tcp.Send(packet.Span);
            else _udp?.SendTo(packet.Span, session.Remote);
        }
        catch (Exception e) when (e is SocketException or IOException or ObjectDisposedException)
        {
            Log.Error($"Send to {session.Remote}", e);
        }
    }

    private sealed class Session(uint id, HelloPayload hello, IPEndPoint remote, TcpConnection? tcp)
    {
        public uint Id { get; } = id;
        public string DeviceId { get; } = hello.DeviceId;
        public IPEndPoint Remote { get; private set; } = remote;
        public TcpConnection? Tcp { get; private set; } = tcp;
        public uint? NextSeq { get; set; }
        public int SampleRate => hello.SampleRate;
        private long _lastSeen = Environment.TickCount64;

        public SessionInfo Info => new(hello.DeviceName, Tcp != null ? Transport.Usb : Transport.WiFi, Remote, hello.SampleRate);

        public void Touch(IPEndPoint from, TcpConnection? tcp)
        {
            _lastSeen = Environment.TickCount64;
            Remote = from;
            Tcp = tcp;
        }

        public bool IsExpired(TimeSpan timeout) => Environment.TickCount64 - _lastSeen > timeout.TotalMilliseconds;
    }

    private sealed class TcpConnection(TcpClient client) : IDisposable
    {
        private readonly object _sendLock = new();

        public void Send(ReadOnlySpan<byte> packet)
        {
            Span<byte> frame = stackalloc byte[packet.Length + 2];
            BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)packet.Length);
            packet.CopyTo(frame[2..]);
            lock (_sendLock) client.Client.Send(frame);
        }

        public void Dispose() => client.Dispose();
    }
}
