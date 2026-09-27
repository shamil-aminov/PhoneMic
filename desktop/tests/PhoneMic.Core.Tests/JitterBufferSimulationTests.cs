using PhoneMic.Core;
using Xunit.Abstractions;

namespace PhoneMic.Core.Tests;

/// <summary>
/// Runs the jitter buffer against a simulated network on a virtual clock, so
/// minutes of streaming take milliseconds and every run is reproducible.
/// </summary>
public class JitterBufferSimulationTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private const int Frame = 480; // 10 ms

    private sealed record Result(int Underruns, double AvgLatencyMs, double MaxLatencyMs, double SilencedVoicedPercent);

    /// <summary>Network model: every packet gets a small random delay, plus occasional stalls that hold everything back.</summary>
    private sealed class Network(double baseMs, double meanJitterMs, double stallsPerMinute, double minStallMs, double maxStallMs, int seed)
    {
        private readonly Random _random = new(seed);
        private double _stallEnd = double.MinValue;
        private double _lastArrival;

        public double Arrival(double sentMs)
        {
            if (sentMs > _stallEnd && _random.NextDouble() < stallsPerMinute / 6000)
                _stallEnd = sentMs + minStallMs + _random.NextDouble() * (maxStallMs - minStallMs);
            var arrival = sentMs + baseMs - meanJitterMs * Math.Log(1 - _random.NextDouble());
            if (sentMs <= _stallEnd) arrival = Math.Max(arrival, _stallEnd + baseMs);
            // Packets on one path arrive in order.
            _lastArrival = Math.Max(arrival, _lastArrival);
            return _lastArrival;
        }
    }

    /// <param name="voiced">Whether the source is making sound at a given time, in ms.</param>
    /// <param name="phoneClock">Phone sample clock relative to the PC's; 1.0003 is 300 ppm fast.</param>
    private static Result Simulate(Network network, Func<double, bool> voiced, double seconds, double phoneClock = 1.0,
        BufferMode mode = BufferMode.Balanced, Action<string>? trace = null)
    {
        double nowMs = 0;
        var buffer = new JitterBuffer(Rate, () => nowMs / 1000) { Profile = BufferProfile.For(mode) };
        var estimator = new JitterEstimator(() => nowMs);
        buffer.Reset(Rate);

        var packets = new Queue<(double Arrival, int Index)>();
        var nextPacket = 0;
        var deliveredMs = 0.0;
        var pcm = new byte[Frame * 2];
        var outBuf = new float[Frame];
        double latencySum = 0, latencyMax = 0;
        long latencyCount = 0, voicedOut = 0, silencedVoicedOut = 0;

        for (var tick = 0; nowMs < seconds * 1000; tick++, nowMs = tick)
        {
            // Phone: a packet is sent once its 10 ms of audio has been captured.
            while ((nextPacket + 1) * 10.0 / phoneClock <= nowMs)
            {
                packets.Enqueue((network.Arrival(nowMs), nextPacket));
                nextPacket++;
            }
            // PC: deliver whatever has arrived.
            while (packets.Count > 0 && packets.Peek().Arrival <= nowMs)
            {
                var index = packets.Dequeue().Index;
                var on = voiced(index * 10.0);
                for (var i = 0; i < Frame; i++)
                {
                    var s = on ? (short)(Math.Sin(2 * Math.PI * 440 * (index * Frame + i) / Rate) * 8000) : (short)0;
                    pcm[2 * i] = (byte)s;
                    pcm[2 * i + 1] = (byte)(s >> 8);
                }
                buffer.WritePcm16(pcm);
                deliveredMs = (index + 1) * 10.0;
                estimator.OnPacket(index * 10.0, buffer.Profile.MaxJitterMs, buffer.Profile.MemorySeconds);
                buffer.JitterMs = estimator.PeakMs;
            }
            // Sound card pulls 10 ms every 10 ms.
            if (tick % 10 == 0 && tick > 0)
            {
                var levelBefore = buffer.LevelMs;
                if (trace != null && tick % 1000 == 0)
                    trace($"t={tick / 1000}s level={levelBefore:F0} target={buffer.TargetMs:F0} peak={estimator.PeakMs:F0} underruns={buffer.Underruns} voiced={voiced(deliveredMs)}");
                buffer.Read(outBuf, Rate, 1);
                if (tick > 2000)
                {
                    latencySum += levelBefore;
                    latencyMax = Math.Max(latencyMax, levelBefore);
                    latencyCount++;
                    // The window just played ends `level` behind the newest delivered sample.
                    if (voiced(deliveredMs - buffer.LevelMs - 5))
                    {
                        voicedOut++;
                        if (outBuf.All(v => v == 0)) silencedVoicedOut++;
                    }
                }
            }
        }
        return new Result(buffer.Underruns, latencySum / latencyCount, latencyMax,
            voicedOut == 0 ? 0 : 100.0 * silencedVoicedOut / voicedOut);
    }

    private static bool Tone(double ms) => true;

    [Fact(Skip = "diagnostic")]
    public void Trace_stable() => Simulate(WiFi(), Speech(7), 120, mode: BufferMode.Stable, trace: output.WriteLine);

    /// <summary>Talk in 0.4-2 s phrases with 0.2-0.8 s pauses, like speech.</summary>
    private static Func<double, bool> Speech(int seed)
    {
        var random = new Random(seed);
        var edges = new List<double>();
        for (double t = 0; t < 3_600_000;)
        {
            t += 400 + random.NextDouble() * 1600;
            edges.Add(t);
            t += 200 + random.NextDouble() * 600;
            edges.Add(t);
        }
        return ms =>
        {
            // Packets are voiced or not as a whole, so judge by the packet a time falls in.
            var i = edges.BinarySearch(Math.Floor(ms / 10) * 10);
            if (i < 0) i = ~i;
            return i % 2 == 0;
        };
    }

    private static Network Usb(int seed = 1) => new(1, 0.5, 0, 0, 0, seed);
    private static Network WiFi(int seed = 1) => new(3, 6, 4, 40, 120, seed);

    private Result Report(string name, Result r)
    {
        output.WriteLine($"{name}: underruns {r.Underruns}, latency avg {r.AvgLatencyMs:F0} max {r.MaxLatencyMs:F0} ms, voiced audio silenced {r.SilencedVoicedPercent:F2}%");
        return r;
    }

    [Fact]
    public void Usb_is_glitch_free_with_low_latency()
    {
        var r = Report("usb tone", Simulate(Usb(), Tone, 120));
        Assert.Equal(0, r.Underruns);
        Assert.True(r.AvgLatencyMs < 50, $"latency {r.AvgLatencyMs}");
    }

    [Fact]
    public void WiFi_speech_rarely_glitches_and_stays_interactive()
    {
        var r = Report("wifi speech balanced", Simulate(WiFi(), Speech(7), 600));
        Assert.True(r.SilencedVoicedPercent < 0.3, $"silenced {r.SilencedVoicedPercent}%");
        Assert.True(r.AvgLatencyMs < 120, $"latency {r.AvgLatencyMs}");
    }

    [Fact]
    public void Modes_trade_latency_for_stability()
    {
        var low = Report("wifi speech low", Simulate(WiFi(), Speech(7), 600, mode: BufferMode.LowLatency));
        var balanced = Report("wifi speech balanced", Simulate(WiFi(), Speech(7), 600, mode: BufferMode.Balanced));
        var stable = Report("wifi speech stable", Simulate(WiFi(), Speech(7), 600, mode: BufferMode.Stable));
        Assert.True(low.AvgLatencyMs < balanced.AvgLatencyMs && balanced.AvgLatencyMs < stable.AvgLatencyMs);
        Assert.True(stable.Underruns < balanced.Underruns && balanced.Underruns <= low.Underruns);
        Assert.True(stable.Underruns <= 2, $"stable underruns {stable.Underruns}");
    }

    [Fact]
    public void Usb_low_latency_mode_is_glitch_free()
    {
        var r = Report("usb speech low", Simulate(Usb(), Speech(9), 300, mode: BufferMode.LowLatency));
        Assert.Equal(0, r.Underruns);
        Assert.True(r.AvgLatencyMs < 35, $"latency {r.AvgLatencyMs}");
    }

    [Fact]
    public void Pauses_absorb_jitter_better_than_a_continuous_tone()
    {
        var tone = Report("wifi tone", Simulate(WiFi(3), Tone, 600));
        var speech = Report("wifi speech", Simulate(WiFi(3), Speech(3), 600));
        Assert.True(speech.Underruns <= tone.Underruns);
    }

    [Theory]
    [InlineData(1.0003)]
    [InlineData(0.9997)]
    public void Clock_drift_does_not_accumulate(double phoneClock)
    {
        var r = Report($"usb drift {phoneClock}", Simulate(Usb(), Tone, 600, phoneClock));
        Assert.Equal(0, r.Underruns);
        Assert.True(r.MaxLatencyMs < 80, $"max latency {r.MaxLatencyMs}");
    }

    [Fact]
    public void Drift_during_speech_is_absorbed_by_pauses()
    {
        var r = Report("usb speech drift", Simulate(Usb(), Speech(5), 600, 1.0005));
        Assert.Equal(0, r.Underruns);
        Assert.True(r.MaxLatencyMs < 80, $"max latency {r.MaxLatencyMs}");
    }
}
