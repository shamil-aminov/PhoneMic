using System.Diagnostics;

namespace PhoneMic.Core;

/// <summary>
/// Measures how late packets arrive compared with the earliest one, i.e. the
/// network jitter, and suggests how much audio the buffer should hold to ride
/// it out.
///
/// Every packet carries a sequence number, so its ideal arrival time is known.
/// The earliest arrival seen recently is the baseline; lateness is how far
/// behind that baseline a packet lands. The baseline creeps upward slowly so a
/// phone whose clock runs a little slow is not mistaken for growing jitter.
///
/// The peak has a long memory, because Wi-Fi hiccups come in waves, but is
/// capped: a stall of half a second (a Wi-Fi scan, for example) cannot be
/// buffered away without making every call laggy, so it is left to glitch once.
/// </summary>
public sealed class JitterEstimator
{
    private const double BaselineCreepMs = 0.005;    // per packet: 0.5 ms/s, covers 500 ppm of clock drift

    private readonly Func<double> _nowMs;
    private readonly List<double> _window = [];
    private double _baseline = double.MaxValue;
    private double _peak;
    private double? _lastMs;

    /// <param name="nowMs">Clock in milliseconds; tests substitute a simulated one.</param>
    public JitterEstimator(Func<double>? nowMs = null)
    {
        if (nowMs == null)
        {
            var clock = Stopwatch.StartNew();
            nowMs = () => clock.Elapsed.TotalMilliseconds;
        }
        _nowMs = nowMs;
    }

    public double PeakMs => _peak;

    public void Reset()
    {
        _baseline = double.MaxValue;
        _peak = 0;
        _lastMs = null;
        _window.Clear();
    }

    /// <param name="mediaMs">Position of the packet in the stream, from its sequence number.</param>
    /// <param name="maxPeakMs">Lateness above this is clipped before it reaches the peak.</param>
    /// <param name="halfLifeSeconds">How fast the remembered peak fades.</param>
    public void OnPacket(double mediaMs, double maxPeakMs = double.MaxValue, double halfLifeSeconds = 30)
    {
        var now = _nowMs();
        var elapsed = _lastMs is { } last ? now - last : 0;
        _lastMs = now;
        var decay = Math.Pow(0.5, elapsed / (halfLifeSeconds * 1000));
        var offset = now - mediaMs;
        _baseline = Math.Min(_baseline + BaselineCreepMs, offset);
        var lateness = offset - _baseline;
        _peak = Math.Max(Math.Min(lateness, maxPeakMs), _peak * decay);
        _window.Add(lateness);
    }

    /// <summary>Summary of lateness since the last call, for the log.</summary>
    public string TakeStats()
    {
        if (_window.Count == 0) return "no packets";
        _window.Sort();
        double P(double q) => _window[Math.Min(_window.Count - 1, (int)(q * _window.Count))];
        var s = $"jitter p50 {P(0.5):F0} p95 {P(0.95):F0} p99 {P(0.99):F0} max {_window[^1]:F0} ms over {_window.Count} packets";
        _window.Clear();
        return s;
    }
}
