using System.Buffers.Binary;
using System.Diagnostics;

namespace PhoneMic.Core;

/// <summary>
/// Absorbs network jitter between the phone and the sound card, and hides the
/// fact that their clocks never run at exactly the same speed.
///
/// Audio is played back <see cref="TargetMs"/> behind the newest sample. The
/// target follows the measured network jitter (<see cref="JitterMs"/>) and is
/// raised further after every underrun, then relaxes over time.
///
/// The fill level is steered towards the target without audible artifacts.
/// Pauses in speech do most of the work: with too much audio buffered, silent
/// stretches are skipped; with too little, they are stretched. Playback speed
/// is also nudged by up to 1% for signals that never pause.
/// </summary>
public sealed class JitterBuffer
{
    private const double FloorRelaxMsPerSecond = 1;
    private const double MaxSpeedAdjust = 0.01;
    private const float SilenceThreshold = 0.003f; // about -50 dBFS
    private const int FadeSamples = 240;

    private readonly object _lock = new();
    private readonly float[] _ring = new float[96000 * 2];
    private readonly Func<double> _now;
    private int _inputRate;

    private long _written;
    private double _readPos;
    private bool _playing;
    private int _fadeIn;
    private BufferProfile _profile = BufferProfile.For(BufferMode.Balanced);
    private double _targetMs;
    private double _jitterMs;
    private double _underrunFloorMs;
    private double _smoothedLevel;
    private double _lastUpdate;
    private int _holdFrames;

    /// <param name="now">Clock in seconds; tests substitute a simulated one.</param>
    public JitterBuffer(int inputRate = 48000, Func<double>? now = null)
    {
        _inputRate = inputRate;
        if (now == null)
        {
            var clock = Stopwatch.StartNew();
            now = () => clock.Elapsed.TotalSeconds;
        }
        _now = now;
        _lastUpdate = _now();
        _targetMs = _profile.MinTargetMs + 20;
    }

    public BufferProfile Profile
    {
        get { lock (_lock) return _profile; }
        set { lock (_lock) _profile = value; }
    }

    /// <summary>Linear gain applied on output.</summary>
    public float Gain { get; set; } = 1f;

    public int Underruns { get; private set; }

    /// <summary>Recent worst-case packet lateness, from <see cref="JitterEstimator"/>.</summary>
    public double JitterMs
    {
        set { lock (_lock) _jitterMs = value; }
    }

    public double TargetMs
    {
        get { lock (_lock) return _targetMs; }
    }

    public double LevelMs
    {
        get { lock (_lock) return (_written - _readPos) * 1000.0 / _inputRate; }
    }

    private int TargetSamples => (int)(_targetMs * _inputRate / 1000);

    /// <summary>Drops everything buffered, e.g. when a new phone connects. Rates up to 96 kHz.</summary>
    /// <param name="headroomMs">
    /// Extra buffering to start with while nothing is known about the network yet.
    /// It relaxes by itself within a minute if the connection turns out to be calm.
    /// </param>
    public void Reset(int inputRate = 0, double headroomMs = 20)
    {
        lock (_lock)
        {
            if (inputRate > 0) _inputRate = Math.Min(inputRate, 96000);
            _written = 0;
            _readPos = 0;
            _playing = false;
            _targetMs = Math.Min(_profile.MinTargetMs + headroomMs, _profile.MaxTargetMs);
            _jitterMs = 0;
            _underrunFloorMs = _targetMs;
            _smoothedLevel = 0;
            _holdFrames = 0;
            _lastUpdate = _now();
            Underruns = 0;
        }
    }

    /// <summary>Appends little-endian 16-bit PCM.</summary>
    public void WritePcm16(ReadOnlySpan<byte> pcm)
    {
        lock (_lock)
        {
            for (var i = 0; i + 1 < pcm.Length; i += 2)
                Push(BinaryPrimitives.ReadInt16LittleEndian(pcm[i..]) / 32768f);
        }
    }

    /// <summary>Fills a hole left by lost packets.</summary>
    public void WriteSilence(int samples)
    {
        lock (_lock)
        {
            for (var i = 0; i < samples; i++) Push(0f);
        }
    }

    private void Push(float sample)
    {
        _ring[_written % _ring.Length] = sample;
        _written++;
        // Never let the writer lap the reader.
        if (_written - _readPos > _ring.Length - 2) _readPos = _written - TargetSamples;
    }

    private void UpdateTarget()
    {
        var now = _now();
        var elapsed = now - _lastUpdate;
        _lastUpdate = now;
        _underrunFloorMs = Math.Max(0, _underrunFloorMs - FloorRelaxMsPerSecond * elapsed);
        var p = _profile;
        _targetMs = Math.Clamp(Math.Max(Math.Min(_jitterMs, p.MaxJitterMs) + p.SafetyMs, _underrunFloorMs), p.MinTargetMs, p.MaxTargetMs);
    }

    private bool IsQuiet(long from, int count)
    {
        for (var i = from; i < from + count; i++)
            if (Math.Abs(_ring[i % _ring.Length]) > SilenceThreshold) return false;
        return true;
    }

    /// <summary>
    /// Fills <paramref name="output"/> with interleaved samples at the sound
    /// card's rate and channel count. Outputs silence while there is not enough
    /// audio buffered.
    /// </summary>
    public void Read(Span<float> output, int outputRate, int outputChannels)
    {
        var frames = output.Length / outputChannels;
        lock (_lock)
        {
            UpdateTarget();
            var level = _written - _readPos;
            var target = TargetSamples;

            if (!_playing)
            {
                if (level >= target)
                {
                    _playing = true;
                    _fadeIn = FadeSamples;
                    _readPos = _written - target;
                    _smoothedLevel = target;
                }
            }
            else if (level > target + _inputRate * 0.15)
            {
                // Far behind, e.g. after the sound card stalled. Jump rather than race.
                _readPos = _written - target;
            }
            else
            {
                var chunk = _inputRate / 200; // 5 ms
                var pos = (long)_readPos;
                // Too much buffered: drop silent stretches, nobody can hear those go.
                while (_written - _readPos > target + _inputRate / 50 && IsQuiet((long)_readPos, chunk))
                    _readPos += chunk;
                // Too little: stretch a pause by 5 ms, if we are in the middle of one.
                if (_holdFrames == 0 && level < target - _inputRate / 100 && level >= chunk
                    && pos >= chunk && IsQuiet(pos - chunk, chunk * 2))
                    _holdFrames = chunk * outputRate / _inputRate;
            }

            // Steer on a smoothed level: the raw one saws by a packet every 10 ms.
            level = _written - _readPos;
            _smoothedLevel += (level - _smoothedLevel) * 0.05;
            var errorMs = (_smoothedLevel - target) * 1000.0 / _inputRate;
            var speed = 1 + Math.Clamp(errorMs * 0.0005, -MaxSpeedAdjust, MaxSpeedAdjust);
            var step = (double)_inputRate / outputRate * speed;
            var gain = Gain;

            for (var f = 0; f < frames; f++)
            {
                float sample = 0;
                if (_holdFrames > 0)
                {
                    _holdFrames--;
                }
                else if (_playing)
                {
                    var left = _written - _readPos;
                    if (left < 2)
                    {
                        _playing = false;
                        Underruns++;
                        _underrunFloorMs = Math.Max(_underrunFloorMs, _targetMs + 10);
                    }
                    else
                    {
                        var i0 = (long)_readPos;
                        var frac = (float)(_readPos - i0);
                        var a = _ring[i0 % _ring.Length];
                        var b = _ring[(i0 + 1) % _ring.Length];
                        sample = (a + (b - a) * frac) * gain;
                        if (_fadeIn > 0)
                        {
                            sample *= 1f - _fadeIn / (float)FadeSamples;
                            _fadeIn--;
                        }
                        // About to run dry: fade out instead of clicking into silence.
                        if (left < FadeSamples) sample *= (float)(left / FadeSamples);
                        sample = Math.Clamp(sample, -1f, 1f);
                        _readPos += step;
                    }
                }
                for (var c = 0; c < outputChannels; c++) output[f * outputChannels + c] = sample;
            }
        }
    }
}
