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
    private double _meter;

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
            ? "Компьютер не подключён к сети"
            : $"{string.Join(", ", addresses)} · порт {_engine.Settings.Port}";
        Log.Info($"Pairing QR: {uri}");
    }

    private void ShowSession(SessionInfo? session)
    {
        var live = session != null;
        LivePanel.Visibility = live && !_qrForced ? Visibility.Visible : Visibility.Collapsed;
        PairPanel.Visibility = live && !_qrForced ? Visibility.Collapsed : Visibility.Visible;
        HideQrButton.Visibility = live && _qrForced ? Visibility.Visible : Visibility.Collapsed;

        StatusDot.Fill = (Brush)FindResource(live ? "Live" : "TextDim");
        StatusText.Foreground = (Brush)FindResource(live ? "Live" : "TextDim");
        StatusPill.Background = (Brush)FindResource(live ? "LiveDim" : "SurfaceHigh");
        StatusText.Text = live ? "Подключено" : "Ожидание телефона";

        if (session != null)
        {
            DeviceNameText.Text = session.DeviceName;
            TransportText.Text = session.Transport == Transport.Usb
                ? "По USB-кабелю"
                : $"По Wi-Fi · {session.Remote.Address}";
        }
        else
        {
            _qrForced = false;
        }
    }

    private void Tick()
    {
        // Fast attack, slow release, on a dB scale like a real meter.
        var peak = _engine.Server.TakePeak();
        var db = 20 * Math.Log10(Math.Max(peak, 1e-4));
        var target = Math.Clamp((db + 60) / 60, 0, 1);
        _meter = target > _meter ? target : Math.Max(target, _meter - 0.03);
        var track = ((FrameworkElement)LevelFill.Parent).ActualWidth;
        LevelFill.Width = track * _meter;
        LevelFill.Background = (Brush)FindResource(db > -1 ? "Error" : "Live");

        if (_engine.Server.Current == null) return;
        var received = _engine.Server.PacketsReceived;
        var lost = _engine.Server.PacketsLost;
        var lossPercent = received + lost == 0 ? 0 : 100.0 * lost / (received + lost);
        StatsText.Text = $"Задержка буфера {_engine.Buffer.TargetMs:F0} мс · потери {lossPercent:F1}%";
    }

    private void LoadDevices()
    {
        _loadingDevices = true;
        var devices = AudioOutput.ListDevices();
        DeviceBox.ItemsSource = devices;
        var current = _engine.Output.DeviceId is { } id ? devices.FirstOrDefault(d => d.Id == id) : null;
        DeviceBox.SelectedItem = current;
        CableWarning.Visibility = devices.Any(d => d.IsVirtualCable) ? Visibility.Collapsed : Visibility.Visible;
        _loadingDevices = false;
        UpdateDeviceHint(null);
    }

    private void UpdateDeviceHint(string? error)
    {
        var device = DeviceBox.SelectedItem as OutputDevice;
        DeviceHint.Foreground = (Brush)FindResource(error != null || device == null ? "Warn" : "TextDim");
        DeviceHint.Text = error ?? device switch
        {
            null => "Выберите устройство, иначе звук никуда не пойдёт.",
            { IsVirtualCable: true } => "В Discord, Zoom, OBS и других программах выберите микрофон «CABLE Output».",
            _ => "Звук телефона будет слышен в этом устройстве. Чтобы программы видели его как микрофон, выберите «CABLE Input».",
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
            BufferMode.LowLatency => "Задержка 20–40 мс. Для USB и отличного Wi-Fi, на плохом будут щелчки.",
            BufferMode.Stable => "Задержка около 100–150 мс, зато без обрывов даже на плохом Wi-Fi.",
            _ => "Сам подстраивается под сеть. Подходит почти всегда.",
        };
    }

    private void AutostartBox_Changed(object sender, RoutedEventArgs e) =>
        Autostart.IsEnabled = AutostartBox.IsChecked == true;

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Появится новый QR-код, а телефоны, сопряжённые раньше, перестанут подключаться, пока не отсканируют его.",
            "Сбросить сопряжение?", MessageBoxButton.OKCancel, MessageBoxImage.Question);
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
