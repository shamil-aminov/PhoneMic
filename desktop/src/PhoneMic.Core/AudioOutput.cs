using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PhoneMic.Core;

public enum OutputError
{
    OpenFailed,
    Disconnected,
}

public sealed record OutputDevice(string Id, string Name)
{
    /// <summary>VB-Audio Virtual Cable's playback side, which other apps see as the microphone "CABLE Output".</summary>
    public bool IsVirtualCable => Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name;
}

/// <summary>Plays a <see cref="JitterBuffer"/> to a WASAPI render device and restarts after device errors.</summary>
public sealed class AudioOutput : IDisposable
{
    private readonly JitterBuffer _buffer;
    private readonly object _lock = new();
    private WasapiOut? _out;
    private string? _deviceId;
    private Timer? _retry;

    public AudioOutput(JitterBuffer buffer) => _buffer = buffer;

    /// <summary>Null once the device plays again.</summary>
    public event Action<OutputError?>? ErrorChanged;

    public string? DeviceId
    {
        get { lock (_lock) return _deviceId; }
    }

    public static IReadOnlyList<OutputDevice> ListDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new OutputDevice(d.ID, d.FriendlyName))
            .OrderByDescending(d => d.IsVirtualCable)
            .ThenBy(d => d.Name)
            .ToList();
    }

    public void Start(string deviceId)
    {
        lock (_lock)
        {
            _deviceId = deviceId;
            StopLocked();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(deviceId);
                var mix = device.AudioClient.MixFormat;
                var provider = new BufferProvider(_buffer, mix.SampleRate, mix.Channels);
                var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
                output.Init(provider);
                output.PlaybackStopped += OnStopped;
                output.Play();
                _out = output;
                Log.Info($"Output started on {device.FriendlyName}, {mix.SampleRate} Hz x{mix.Channels}");
                ErrorChanged?.Invoke(null);
            }
            catch (Exception e)
            {
                Log.Error($"Starting output {deviceId}", e);
                ErrorChanged?.Invoke(OutputError.OpenFailed);
                ScheduleRetry();
            }
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception == null) return;
        Log.Error("Output stopped", e.Exception);
        ErrorChanged?.Invoke(OutputError.Disconnected);
        ScheduleRetry();
    }

    private void ScheduleRetry()
    {
        _retry?.Dispose();
        _retry = new Timer(_ =>
        {
            var id = DeviceId;
            if (id != null) Start(id);
        }, null, 2000, Timeout.Infinite);
    }

    private void StopLocked()
    {
        _retry?.Dispose();
        _retry = null;
        if (_out == null) return;
        _out.PlaybackStopped -= OnStopped;
        _out.Dispose();
        _out = null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _deviceId = null;
            StopLocked();
        }
    }

    private sealed class BufferProvider(JitterBuffer buffer, int rate, int channels) : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);

        public int Read(byte[] data, int offset, int count)
        {
            var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(data.AsSpan(offset, count));
            buffer.Read(floats[..(floats.Length / channels * channels)], rate, channels);
            return count;
        }
    }
}
