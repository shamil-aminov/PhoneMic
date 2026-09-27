using System.Text.Json;

namespace PhoneMic.Core;

public sealed class Settings
{
    private static readonly string FilePath = Path.Combine(Log.Directory, "settings.json");

    public string Token { get; set; } = Pairing.NewToken();
    public int Port { get; set; } = Protocol.DefaultPort;
    public string? OutputDeviceId { get; set; }
    public float Gain { get; set; } = 1f;
    public BufferMode BufferMode { get; set; } = BufferMode.Balanced;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Error("Loading settings", e);
        }
        var fresh = new Settings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException e)
        {
            Log.Error("Saving settings", e);
        }
    }
}
