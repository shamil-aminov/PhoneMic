using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PhoneMic.Core;
using QRCoder;

namespace PhoneMic;

public partial class MainWindow : Window
{
    private readonly Engine _engine;
    private readonly DispatcherTimer _meterTimer;
    private bool _allowClose;
    private bool _hintShown;
    private bool _loadingDevices;
    private bool _qrForced;
    private DateTime _clippedUntil;
    private DateTime? _previewStart;

    /// <summary>Raised the first time the window is closed into the tray.</summary>
    public event Action? HiddenToTray;

    public MainWindow(Engine engine)
    {
        _engine = engine;
        InitializeComponent();

        GainSlider.Value = Math.Round(engine.Settings.Gain * 100);
        AutostartBox.IsChecked = Autostart.IsEnabled;
        (engine.Settings.BufferMode switch
        {
            BufferMode.LowLatency => ModeLow,
            BufferMode.Stable => ModeStable,
            _ => ModeBalanced,
        }).IsChecked = true;
        LoadDevices();
        RefreshQr();
        ShowSession(engine.Server.Current);

        engine.Server.SessionChanged += s => Dispatcher.BeginInvoke(() => ShowSession(s));
        engine.Output.ErrorChanged += e => Dispatcher.BeginInvoke(() => UpdateDeviceHint(e));
        NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.BeginInvoke(RefreshQr);

        _meterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick(), Dispatcher);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _meterTimer.Start();
            else _meterTimer.Stop();
        };
    }

    public void AllowClose() => _allowClose = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_allowClose) return;
        // The window is only a control panel; the microphone keeps working from the tray.
        e.Cancel = true;
        Hide();
        if (!_hintShown)
        {
            _hintShown = true;
            HiddenToTray?.Invoke();
        }
    }

    private void RefreshQr()
    {
        var uri = _engine.PairingUri(out var addresses);
        using var data = new QRCodeGenerator().CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8, [0x0E, 0x11, 0x13], [0xFF, 0xFF, 0xFF], false);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(png);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        QrImage.Source = image;
        AddressText.Text = addresses.Count == 0
            ? Text.NoNetwork
            : $"{string.Join(", ", addresses)} · {Text.Port(_engine.Settings.Port)}";
        Log.Info($"Pairing QR: {uri}");
    }

    /// <summary>Made-up data for the design preview: a phone on USB, talking.</summary>
    public static SessionInfo PreviewSession { get; } =
        new("Pixel 8", Transport.Usb, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0), 48000);

    public void ShowPreview(bool live)
    {
        _previewStart = DateTime.UtcNow;
        if (!live) return;
        ShowSession(PreviewSession);
        StatsText.Text = Text.Stats(32, 0);
    }

    private void ShowSession(SessionInfo? session)
    {
        var live = session != null;
        LivePanel.Visibility = live && !_qrForced ? Visibility.Visible : Visibility.Collapsed;
        PairPanel.Visibility = live && !_qrForced ? Visibility.Collapsed : Visibility.Visible;
        HideQrButton.Visibility = live && _qrForced ? Visibility.Visible : Visibility.Collapsed;

        StatusDot.Fill = (Brush)FindResource(live ? "Live" : "TextFaint");
        StatusText.Foreground = (Brush)FindResource(live ? "Text" : "TextDim");
        StatusText.Text = live ? Text.Connected : Text.WaitingForPhone;

        if (session != null)
        {
            DeviceNameText.Text = session.DeviceName;
            TransportText.Text = session.Transport == Transport.Usb
                ? Text.UsbCable
                : $"Wi-Fi · {session.Remote.Address}";
        }
        else
        {
            _qrForced = false;
        }
    }

    private void Tick()
    {
        if (_previewStart is { } start)
        {
            // Speech-like: syllables inside phrases inside pauses.
            var t = (DateTime.UtcNow - start).TotalSeconds;
            Wave.Level = 0.02 + 0.5 * Math.Abs(Math.Sin(t * 5.1)) * Math.Max(0, Math.Sin(t * 0.9 + 1.2));
            return;
        }
        var peak = _engine.Server.TakePeak();
        Wave.Level = peak;
        // Anything at full scale after the volume boost is clipping; keep the warning up a moment.
        if (peak >= 0.99f) _clippedUntil = DateTime.UtcNow.AddSeconds(1.5);

        if (_engine.Server.Current == null) return;
        var received = _engine.Server.PacketsReceived;
        var lost = _engine.Server.PacketsLost;
        var lossPercent = received + lost == 0 ? 0 : 100.0 * lost / (received + lost);
        var clipping = DateTime.UtcNow < _clippedUntil;
        StatsText.Foreground = (Brush)FindResource(clipping ? "Error" : "TextDim");
        StatsText.Text = clipping
            ? Text.Clipping
            : Text.Stats(_engine.Buffer.TargetMs, lossPercent);
    }

    private void LoadDevices()
    {
        _loadingDevices = true;
        var devices = AudioOutput.ListDevices();
        DeviceBox.ItemsSource = devices;
        // In the preview nothing is playing, so show what would be picked.
        var current = _engine.Output.DeviceId is { } id ? devices.FirstOrDefault(d => d.Id == id) : _engine.PickDevice(devices);
        DeviceBox.SelectedItem = current;
        CableWarning.Visibility = devices.Any(d => d.IsVirtualCable) ? Visibility.Collapsed : Visibility.Visible;
        _loadingDevices = false;
        UpdateDeviceHint(null);
    }

    private void UpdateDeviceHint(OutputError? error)
    {
        var device = DeviceBox.SelectedItem as OutputDevice;
        DeviceHint.Foreground = (Brush)FindResource(error != null || device == null ? "Warn" : "TextDim");
        DeviceHint.Text = error switch
        {
            OutputError.OpenFailed => Text.OutputOpenFailed,
            OutputError.Disconnected => Text.OutputLost,
            _ => device switch
            {
                null => Text.NoDevice,
                { IsVirtualCable: true } => Text.CableChosen,
                _ => Text.OtherDeviceChosen,
            },
        };
    }

    private void DeviceBox_DropDownOpened(object? sender, EventArgs e) => LoadDevices();

    private void DeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDevices || DeviceBox.SelectedItem is not OutputDevice device) return;
        if (device.Id == _engine.Output.DeviceId) return;
        _engine.SelectDevice(device);
        UpdateDeviceHint(null);
    }

    private void GainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_engine == null) return;
        _engine.SetGain((float)(e.NewValue / 100));
        GainText.Text = $"{e.NewValue:F0}%";
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_engine == null) return;
        var mode = sender == ModeLow ? BufferMode.LowLatency : sender == ModeStable ? BufferMode.Stable : BufferMode.Balanced;
        if (mode != _engine.Settings.BufferMode) _engine.SetBufferMode(mode);
        ModeHint.Text = mode switch
        {
            BufferMode.LowLatency => Text.ModeLowHint,
            BufferMode.Stable => Text.ModeStableHint,
            _ => Text.ModeBalancedHint,
        };
    }

    private void AutostartBox_Changed(object sender, RoutedEventArgs e) =>
        Autostart.IsEnabled = AutostartBox.IsChecked == true;

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            Text.ResetPairingExplained,
            Text.ResetPairingQuestion, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        _engine.ResetPairing();
        RefreshQr();
    }

    private void ShowQr_Click(object sender, RoutedEventArgs e)
    {
        _qrForced = true;
        ShowSession(_engine.Server.Current);
    }

    private void HideQr_Click(object sender, RoutedEventArgs e)
    {
        _qrForced = false;
        ShowSession(_engine.Server.Current);
    }

    private void DownloadCable_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true });

    private void OpenLog_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
}
