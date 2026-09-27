using Microsoft.Win32;

namespace PhoneMic;

/// <summary>Start with Windows via the per-user Run key, hidden in the tray.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "PhoneMic";

    private static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string value && value == Command;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, Command);
            else key.DeleteValue(Name, false);
        }
    }
}
