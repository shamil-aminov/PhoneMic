using System.Net.Sockets;
using System.Windows;
using PhoneMic.Core;

namespace PhoneMic;

public partial class App : Application
{
    private const string InstanceName = "PhoneMic.SingleInstance";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private Engine? _engine;
    private Tray? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.FirstOrDefault(a => a.StartsWith("--preview")) is { } preview)
        {
            StartPreview(live: preview != "--preview=pairing");
            return;
        }

        _instanceMutex = new Mutex(true, InstanceName, out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        if (!first)
        {
            // Already running, probably hidden in the tray: bring that one forward instead.
            _showSignal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) => Log.Error("Unhandled", args.Exception);
        Log.Info($"PhoneMic {typeof(App).Assembly.GetName().Version} starting");

        try
        {
            _engine = new Engine(Settings.Load());
        }
        catch (SocketException ex)
        {
            Log.Error("Starting server", ex);
            MessageBox.Show(
                "Порт 50505 уже занят другой программой. Закройте её и запустите PhoneMic снова.",
                "PhoneMic", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _tray = new Tray(ShowWindow, ExitApp);
        _tray.SetConnected(_engine.Server.Current);
        _window = new MainWindow(_engine);
        _window.HiddenToTray += () => _tray.ShowHint("PhoneMic продолжает работать здесь. Выход — через меню значка.");
        _engine.Server.SessionChanged += s => Dispatcher.BeginInvoke(() => _tray.SetConnected(s));

        if (!e.Args.Contains("--minimized")) ShowWindow();

        new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(ShowWindow);
        }) { IsBackground = true, Name = "PhoneMic show signal" }.Start();
    }

    /// <summary>
    /// `--preview` or `--preview=pairing`: the window with made-up data and no
    /// audio or networking at all, for screenshots and design work. It runs
    /// beside a real instance without touching it.
    /// </summary>
    private void StartPreview(bool live)
    {
        _engine = new Engine(new Settings { Token = "EXAMPLE7", Transient = true }, audio: false);
        _window = new MainWindow(_engine);
        _window.AllowClose();
        _window.Closed += (_, _) => Shutdown();
        _window.ShowPreview(live);
        _window.Show();
    }

    private void ShowWindow()
    {
        if (_window == null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApp()
    {
        _window?.AllowClose();
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _engine?.Dispose();
        Log.Info("PhoneMic exited");
        base.OnExit(e);
    }
}
