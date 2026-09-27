namespace PhoneMic.Core;

/// <summary>Plain text log in %APPDATA%\PhoneMic\log.txt, trimmed on start.</summary>
public static class Log
{
    private static readonly object Lock = new();
    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoneMic");
    public static string FilePath { get; } = Path.Combine(Directory, "log.txt");

    static Log()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000) File.Delete(FilePath);
        }
        catch (IOException)
        {
        }
    }

    public static void Info(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        System.Diagnostics.Debug.WriteLine(line);
        lock (Lock)
        {
            try
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }
    }

    public static void Error(string message, Exception e) => Info($"ERROR {message}: {e.GetType().Name}: {e.Message}");
}
