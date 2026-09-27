using System.Windows;
using System.Windows.Media;

namespace PhoneMic;

/// <summary>
/// The phone app's wave, drawn on the PC from the audio that actually arrives:
/// a glowing ribbon of thin lines that swells with the voice. The maths is the
/// same as android/.../ui/Wave.kt, so both screens move alike.
/// </summary>
public sealed class WaveView : FrameworkElement
{
    private const int Lines = 30;
    private const int Points = 110;

    private static readonly Color Cyan = Color.FromRgb(0x22, 0xD3, 0xEE);
    private static readonly Color Blue = Color.FromRgb(0x60, 0xA5, 0xFA);
    private static readonly Color Violet = Color.FromRgb(0xA7, 0x8B, 0xFA);

    private readonly double[] _f = new double[Points + 1];
    private readonly double[] _g = new double[Points + 1];
    private readonly Pen[] _glow = new Pen[Lines];
    private readonly Pen[] _line = new Pen[Lines];
    private readonly DateTime _start = DateTime.UtcNow;
    private double _energy;

    /// <summary>Loudest recent sample, 0..1. Set it as often as new audio arrives.</summary>
    public double Level { get; set; }

    public WaveView()
    {
        BuildPens();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) CompositionTarget.Rendering += OnFrame;
            else CompositionTarget.Rendering -= OnFrame;
        };
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        // Loudness on a dB scale, rising fast and falling slowly like a meter.
        var loudness = Math.Clamp((20 * Math.Log10(Math.Max(Level, 1e-4)) + 50) / 44, 0, 1);
        _energy += (loudness - _energy) * (loudness > _energy ? 0.35 : 0.08);
        InvalidateVisual();
    }

    private void BuildPens()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, Cyan.R, Cyan.G, Cyan.B), 0),
                new GradientStop(Cyan, 0.2),
                new GradientStop(Blue, 0.35),
                new GradientStop(Violet, 0.5),
                new GradientStop(Blue, 0.65),
                new GradientStop(Cyan, 0.8),
                new GradientStop(Color.FromArgb(0, Cyan.R, Cyan.G, Cyan.B), 1),
            },
        };
        brush.Freeze();
        for (var i = 0; i < Lines; i++)
        {
            // Lines facing the viewer are brighter than the ones seen edge-on.
            var facing = 0.45 + 0.55 * Math.Abs(Math.Cos(Math.PI * i / Lines));
            var glowBrush = brush.Clone();
            glowBrush.Opacity = 0.05 * facing;
            glowBrush.Freeze();
            var lineBrush = brush.Clone();
            lineBrush.Opacity = 0.45 * facing;
            lineBrush.Freeze();
            _glow[i] = new Pen(glowBrush, 5);
            _line[i] = new Pen(lineBrush, 1);
            _glow[i].Freeze();
            _line[i].Freeze();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var t = (DateTime.UtcNow - _start).TotalSeconds;
        var mid = h / 2;
        var breathing = 0.07 + 0.03 * Math.Sin(t * 1.3);
        var amplitude = h * 0.46 * (0.012 + breathing + (1 - breathing) * _energy);

        for (var j = 0; j <= Points; j++)
        {
            var x = (double)j / Points;
            var envelope = Math.Exp(-Math.Pow((x - 0.5) / 0.25, 2));
            _f[j] = envelope * (0.62 * Math.Sin(x * Math.Tau * 1.7 + t * 1.8) + 0.38 * Math.Sin(x * Math.Tau * 3.3 - t * 2.5));
            _g[j] = envelope * (0.55 * Math.Sin(x * Math.Tau * 2.4 - t * 1.4 + 1.3) + 0.45 * Math.Sin(x * Math.Tau * 4.6 + t * 3.1));
        }

        for (var i = 0; i < Lines; i++)
        {
            var theta = Math.PI * i / Lines;
            var c = Math.Cos(theta);
            var s = Math.Sin(theta);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, mid + amplitude * (c * _f[0] + s * _g[0])), false, false);
                for (var j = 1; j <= Points; j++)
                    ctx.LineTo(new Point(w * j / Points, mid + amplitude * (c * _f[j] + s * _g[j])), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, _glow[i], geometry);
            dc.DrawGeometry(null, _line[i], geometry);
        }
    }
}
