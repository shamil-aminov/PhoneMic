using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PhoneMic.Core;

// Test harness for checking the audio path end to end without a human listening.
//
//   probe devices
//   probe fakephone <host> <token> [udp|tcp] [seconds] [freqHz] [lossPercent] [port]
//   probe listen <token> [seconds] [out.wav]      run a bare server and analyse what arrives
//   probe record <device substring> [seconds]     capture from e.g. "CABLE Output" and analyse

var cmd = args.FirstOrDefault() ?? "";
switch (cmd)
{
    case "devices":
        using (var e = new MMDeviceEnumerator())
        {
            foreach (var d in e.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
                Console.WriteLine($"{d.DataFlow,-8} {d.FriendlyName}  [{d.AudioClient.MixFormat.SampleRate} Hz x{d.AudioClient.MixFormat.Channels}]");
        }
        break;
    case "fakephone":
        await FakePhone(args[1], args[2], Arg(3, "udp"), int.Parse(Arg(4, "5")), int.Parse(Arg(5, "1000")), int.Parse(Arg(6, "0")),
            int.Parse(Arg(7, Protocol.DefaultPort.ToString())));
        break;
    case "listen":
        Listen(args[1], int.Parse(Arg(2, "10")), args.Length > 3 ? args[3] : null);
        break;
    case "record":
        Record(args[1], int.Parse(Arg(2, "5")), args.Length > 3 ? args[3] : null);
        break;
    default:
        Console.WriteLine("usage: devices | fakephone | listen | record");
        return 1;
}
return 0;

string Arg(int i, string fallback) => args.Length > i ? args[i] : fallback;

static async Task FakePhone(string host, string token, string transport, int seconds, int freq, int lossPercent, int port)
{
    const int rate = 48000, frame = 480;
    var session = (uint)Random.Shared.Next();
    var w = new PacketWriter();
    var rx = new byte[Protocol.MaxPacketSize];
    var target = new IPEndPoint(IPAddress.Parse(host), port);

    Func<ReadOnlyMemory<byte>, Task> send;
    Func<Task<byte[]>> receive;
    if (transport == "tcp")
    {
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(target);
        var stream = tcp.GetStream();
        send = async p =>
        {
            var framed = new byte[p.Length + 2];
            BinaryPrimitives.WriteUInt16LittleEndian(framed, (ushort)p.Length);
            p.CopyTo(framed.AsMemory(2));
            await stream.WriteAsync(framed);
        };
        receive = async () =>
        {
            var len = new byte[2];
            await stream.ReadExactlyAsync(len);
            var body = new byte[BinaryPrimitives.ReadUInt16LittleEndian(len)];
            await stream.ReadExactlyAsync(body);
            return body;
        };
    }
    else
    {
        var udp = new UdpClient();
        udp.Connect(target);
        send = async p => await udp.SendAsync(p);
        receive = async () => (await udp.ReceiveAsync()).Buffer;
    }

    await send(w.Begin(Protocol.Hello, session).Str(token).Str("fake-device").Str("FakePhone").U32(rate).U8(1).Memory);
    var welcome = await receive().WaitAsync(TimeSpan.FromSeconds(2));
    var reader = new PacketReader(welcome);
    Console.WriteLine($"WELCOME status={reader.U8()} pc={reader.Str()}");

    var samples = new short[frame];
    var phase = 0.0;
    var clock = Stopwatch.StartNew();
    var total = seconds * rate / frame;
    var dropped = 0;
    for (uint seq = 0; seq < total; seq++)
    {
        for (var i = 0; i < frame; i++)
        {
            samples[i] = (short)(Math.Sin(phase) * 8000);
            phase += 2 * Math.PI * freq / rate;
        }
        if (Random.Shared.Next(100) < lossPercent) dropped++;
        else await send(w.Begin(Protocol.Audio, session).U32(seq).Samples(samples).Memory);
        if (seq % 50 == 0) await send(w.Begin(Protocol.Ping, session).U64((ulong)clock.ElapsedMilliseconds).Memory);
        var due = (seq + 1) * 10.0;
        var wait = due - clock.Elapsed.TotalMilliseconds;
        if (wait > 1) await Task.Delay(TimeSpan.FromMilliseconds(wait));
    }
    await send(w.Begin(Protocol.Bye, session).Memory);
    Console.WriteLine($"Sent {total - dropped} packets, deliberately dropped {dropped}");
}

static void Listen(string token, int seconds, string? wavPath)
{
    var buffer = new JitterBuffer();
    using var server = new AudioServer(buffer, () => token);
    server.SessionChanged += s => Console.WriteLine(s == null ? "disconnected" : $"connected {s.DeviceName} via {s.Transport} {s.Remote}");
    server.Start();

    const int rate = 48000;
    var output = new List<float>();
    var chunk = new float[480];
    var clock = Stopwatch.StartNew();
    for (long n = 0; clock.Elapsed.TotalSeconds < seconds; n++)
    {
        buffer.Read(chunk, rate, 1);
        output.AddRange(chunk);
        var wait = (n + 1) * 10 - clock.ElapsedMilliseconds;
        if (wait > 0) Thread.Sleep((int)wait);
    }
    Console.WriteLine($"server: received={server.PacketsReceived} lost={server.PacketsLost} underruns={buffer.Underruns} target={buffer.TargetMs:F0}ms");
    if (wavPath != null)
    {
        using var wav = new WaveFileWriter(wavPath, WaveFormat.CreateIeeeFloatWaveFormat(rate, 1));
        wav.WriteSamples(output.ToArray(), 0, output.Count);
    }
    Analysis.Print(output.ToArray(), rate);
}

static void Record(string deviceName, int seconds, string? wavPath)
{
    using var e = new MMDeviceEnumerator();
    var device = e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
        .FirstOrDefault(d => d.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No capture device matching '{deviceName}'");
    using var capture = new WasapiCapture(device, true, 20);
    var format = capture.WaveFormat;
    var mono = new List<float>();
    capture.DataAvailable += (_, a) =>
    {
        var provider = new WaveInProviderAdapter(format, a.Buffer, a.BytesRecorded);
        var floats = provider.ToMonoFloats();
        lock (mono) mono.AddRange(floats);
    };
    capture.StartRecording();
    Console.WriteLine($"Recording {device.FriendlyName} ({format}) for {seconds}s");
    Thread.Sleep(seconds * 1000);
    capture.StopRecording();
    Thread.Sleep(200);
    lock (mono)
    {
        if (wavPath != null)
        {
            using var wav = new WaveFileWriter(wavPath, WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, 1));
            wav.WriteSamples(mono.ToArray(), 0, mono.Count);
        }
        Analysis.Print(mono.ToArray(), format.SampleRate, skipStart: true);
    }
}

sealed class WaveInProviderAdapter(WaveFormat format, byte[] buffer, int bytes)
{
    public float[] ToMonoFloats()
    {
        var ch = format.Channels;
        if (format.BitsPerSample == 32) // WASAPI shared mode captures float
        {
            var f = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytes));
            var r = new float[f.Length / ch];
            for (var i = 0; i < r.Length; i++) r[i] = f[i * ch];
            return r;
        }
        if (format.BitsPerSample == 16)
        {
            var s = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, bytes));
            var r = new float[s.Length / ch];
            for (var i = 0; i < r.Length; i++) r[i] = s[i * ch] / 32768f;
            return r;
        }
        throw new NotSupportedException(format.ToString());
    }
}

static class Analysis
{
    /// <summary>Dominant frequency, level and dropouts (10 ms windows that fall silent in the middle of the signal).</summary>
    public static void Print(float[] x, int rate, bool skipStart = false)
    {
        if (x.Length == 0)
        {
            Console.WriteLine("RESULT: no audio");
            return;
        }
        var win = rate / 100;
        var rms = new List<double>();
        for (var i = 0; i + win <= x.Length; i += win)
        {
            double sum = 0;
            for (var j = i; j < i + win; j++) sum += x[j] * x[j];
            rms.Add(Math.Sqrt(sum / win));
        }
        var loud = rms.Where(r => r > 0.01).ToList();
        if (loud.Count == 0)
        {
            Console.WriteLine($"RESULT: silence ({x.Length / (double)rate:F1}s captured)");
            return;
        }
        // Skip the first second: WASAPI capture itself glitches while starting up.
        var first = rms.FindIndex(r => r > 0.01);
        if (first >= 0 && skipStart) first = Math.Min(first + 100, rms.Count - 1);
        var last = rms.FindLastIndex(r => r > 0.01);
        if (last <= first)
        {
            Console.WriteLine("RESULT: signal too short to analyse");
            return;
        }
        var median = loud.OrderBy(r => r).ElementAt(loud.Count / 2);
        var dropouts = 0;
        var where = new List<string>();
        for (var i = first; i <= last; i++)
        {
            if (rms[i] >= median * 0.3) continue;
            dropouts++;
            if (i == first || rms[i - 1] >= median * 0.3) where.Add($"{i * 10}ms");
        }
        if (where.Count > 0) Console.WriteLine($"dropouts start at: {string.Join(", ", where.Take(30))}");

        var crossings = 0;
        var start = first * win;
        var end = (last + 1) * win;
        for (var i = start + 1; i < end; i++)
            if (x[i - 1] < 0 && x[i] >= 0) crossings++;
        var freq = crossings / ((end - start) / (double)rate);

        Console.WriteLine($"RESULT: signal {(last - first + 1) * 10} ms, freq ~{freq:F0} Hz, rms {20 * Math.Log10(median):F1} dBFS, dropout windows {dropouts} ({100.0 * dropouts / (last - first + 1):F2}%)");
    }
}
