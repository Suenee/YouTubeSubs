using System.Text.Json;

namespace YouTubeSubs;

internal sealed class AppConfig
{
    public string Logging { get; set; } = "off";
    public int Samples { get; set; }
    public string LastFormat { get; set; } = "srt";
    public string LastOutputDirectory { get; set; } = "";
    public Dictionary<string, double> PhaseSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["metadata"] = 0.8, ["transcripts"] = 1.0, ["download"] = 0.8, ["video"] = 10.0, ["audio"] = 5.0, ["format"] = 0.1, ["save"] = 0.1,
    };

    public static string AppDirectory { get { var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); var path = Path.Combine(root, "YouTubeSubs"); Directory.CreateDirectory(path); return path; } }
    public static string ConfigPath => Path.Combine(AppDirectory, "config.json");
    public static AppConfig Load()
    {
        try { if (!File.Exists(ConfigPath)) return new AppConfig(); var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new AppConfig(); config.Normalize(); return config; }
        catch { return new AppConfig(); }
    }
    public void Save() { Normalize(); try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions)); } catch { } }
    private void Normalize()
    {
        if (Logging is not ("off" or "single" or "all")) Logging = "off";
        if (LastFormat is not ("srt" or "sub" or "txt" or "vtt")) LastFormat = "srt";
        var defaults = new Dictionary<string, double> { ["metadata"] = 0.8, ["transcripts"] = 1.0, ["download"] = 0.8, ["video"] = 10.0, ["audio"] = 5.0, ["format"] = 0.1, ["save"] = 0.1 };
        foreach (var item in defaults) PhaseSeconds.TryAdd(item.Key, item.Value);
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

internal static class AppLog
{
    private static string _mode = "off"; private static readonly object Sync = new(); private static string LogPath => Path.Combine(AppConfig.AppDirectory, "ytsubs.log");
    public static void Initialize(string mode) { _mode = mode is "single" or "all" ? mode : "off"; if (_mode == "single") { try { File.WriteAllText(LogPath, string.Empty); } catch { } } }
    public static void Write(string message) { if (_mode == "off") return; lock (Sync) { try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}"); } catch { } } }
}
