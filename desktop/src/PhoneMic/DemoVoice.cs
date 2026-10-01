namespace PhoneMic;

/// <summary>
/// Six seconds of made-up speech, looping on the wall clock: two phrases of
/// syllables with pauses around them. The preview animates to it, and
/// android/.../DemoAnimationTest.kt renders the phone from the same formula at
/// the same moments, so the two waves in the README animation move together.
/// </summary>
public static class DemoVoice
{
    public const double Period = 6;

    public static double Now => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds % Period;

    /// <summary>Loudest sample at <paramref name="t"/> seconds into the loop, 0..1.</summary>
    public static double Level(double t)
    {
        var phrase = Phrase(t, 0.5, 2.5) + Phrase(t, 3.1, 5.4);
        var syllable = Math.Pow(Math.Abs(Math.Sin(Math.PI * 4.3 * t)), 1.3);
        return 0.003 + 0.5 * phrase * syllable;
    }

    private static double Phrase(double t, double from, double to) => Ease((t - from) / 0.12) * Ease((to - t) / 0.12);

    private static double Ease(double x)
    {
        var c = Math.Clamp(x, 0, 1);
        return c * c * (3 - 2 * c);
    }
}
