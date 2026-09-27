using System.Diagnostics;

namespace PhoneMic.Core;

/// <summary>
/// Makes USB work without any setup: for every phone adb can see, forwards the
/// phone's 127.0.0.1:port to this PC with <c>adb reverse</c>. Needs USB
/// debugging on the phone; silently does nothing when adb is not installed.
/// </summary>
public sealed class UsbBridge : IDisposable
{
    private readonly int _port;
    private readonly string? _adb;
    private readonly HashSet<string> _forwarded = [];
    private readonly Timer _timer;
    private int _busy;

    public UsbBridge(int port)
    {
        _port = port;
        _adb = FindAdb();
        Log.Info(_adb == null ? "adb not found, USB mode unavailable" : $"Using adb at {_adb}");
        _timer = new Timer(_ => Poll(), null, 0, 3000);
    }

    public bool AdbAvailable => _adb != null;

    /// <summary>Serials of phones currently forwarded.</summary>
    public IReadOnlyCollection<string> Devices
    {
        get { lock (_forwarded) return _forwarded.ToList(); }
    }

    private static string? FindAdb()
    {
        var candidates = new List<string>();
        foreach (var root in new[] { Environment.GetEnvironmentVariable("ANDROID_HOME"), Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") })
            if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe"));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim(), "adb.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private void Poll()
    {
        if (_adb == null || Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var online = Run("devices").Split('\n')
                .Select(l => l.Trim().Split('\t'))
                .Where(p => p.Length == 2 && p[1] == "device")
                .Select(p => p[0])
                .ToHashSet();

            lock (_forwarded) _forwarded.IntersectWith(online);
            foreach (var serial in online)
            {
                // Checked every time: an adb server restart (other tools bundle
                // their own adb and restart it freely) silently drops the rule.
                if (Run($"-s {serial} reverse --list").Contains($"tcp:{_port} tcp:{_port}"))
                {
                    lock (_forwarded) _forwarded.Add(serial);
                    continue;
                }
                Run($"-s {serial} reverse tcp:{_port} tcp:{_port}");
                lock (_forwarded) _forwarded.Add(serial);
                Log.Info($"adb reverse set up for {serial}");
            }
        }
        catch (Exception e)
        {
            Log.Error("adb", e);
        }
        finally
        {
            _busy = 0;
        }
    }

    private string Run(string args)
    {
        using var p = Process.Start(new ProcessStartInfo(_adb!, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var output = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(10_000))
        {
            p.Kill();
            throw new TimeoutException($"adb {args}");
        }
        return output.Result;
    }

    public void Dispose() => _timer.Dispose();
}
