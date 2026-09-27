namespace PhoneMic.Core;

public enum BufferMode
{
    LowLatency,
    Balanced,
    Stable,
}

/// <summary>How the jitter buffer trades latency against glitches.</summary>
/// <param name="MinTargetMs">Never buffer less than this, however calm the network.</param>
/// <param name="MaxTargetMs">Never buffer more than this, however bad it gets.</param>
/// <param name="MaxJitterMs">Lateness beyond this counts as an outage to glitch through, not jitter to buffer for.</param>
/// <param name="SafetyMs">Margin on top of the measured jitter.</param>
/// <param name="MemorySeconds">Half-life of the remembered worst jitter; longer means fewer surprises and more latency.</param>
public sealed record BufferProfile(double MinTargetMs, double MaxTargetMs, double MaxJitterMs, double SafetyMs, double MemorySeconds)
{
    public static BufferProfile For(BufferMode mode) => mode switch
    {
        BufferMode.LowLatency => new(20, 80, 60, 10, 15),
        BufferMode.Stable => new(80, 300, 250, 30, 180),
        _ => new(30, 150, 100, 15, 30),
    };
}
