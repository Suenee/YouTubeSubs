using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeSubs;

internal sealed class BrollSettings
{
    public List<string> Roots { get; set; } = new()
    {
        @"D:\WORK\Sueneé Universe\BROLL",
        @"N:\WORK\Sueneé Universe\BROLL",
    };
    public int MaxId { get; set; } = 9999;
    public int IdMinDigits { get; set; } = 3;
    public int ClipNameMaxWords { get; set; } = 4;
    public int UniqueNameMaxWords { get; set; } = 8;
    public List<string> VideoExtensions { get; set; } = new()
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".wmv", ".webm", ".mpg", ".mpeg", ".m2ts", ".mts", ".ts",
    };
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public void Normalize()
    {
        Roots = Roots.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Roots.Count == 0)
        {
            Roots.Add(@"D:\WORK\Sueneé Universe\BROLL");
            Roots.Add(@"N:\WORK\Sueneé Universe\BROLL");
        }
        MaxId = Math.Clamp(MaxId, 1, 999999);
        IdMinDigits = Math.Clamp(IdMinDigits, 1, 8);
        ClipNameMaxWords = Math.Clamp(ClipNameMaxWords, 1, 12);
        UniqueNameMaxWords = Math.Clamp(UniqueNameMaxWords, ClipNameMaxWords, 20);
        VideoExtensions = VideoExtensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.Trim().ToLowerInvariant())
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
            .Where(extension => extension.Length > 1 && extension.Skip(1).All(ch => char.IsLetterOrDigit(ch)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (VideoExtensions.Count == 0)
            VideoExtensions.AddRange(new[] { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".wmv", ".webm", ".mpg", ".mpeg", ".m2ts", ".mts", ".ts" });
    }

    public bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}

internal sealed class AppConfig
{
    public string Logging { get; set; } = "single";
    public int Samples { get; set; }
    public string LastFormat { get; set; } = "srt";
    public string LastOutputDirectory { get; set; } = "";
    public string EditingRoot { get; set; } = @"N:\WORK\Sueneé Universe\EDITING";
    public int ClipNameMaxWords { get; set; } = 4;
    public string AvMarkerHtml { get; set; } = "VLC AV {id}";
    public string BrollMarkerHtml { get; set; } = "VLC LOOP {id}";
    public BrollSettings Broll { get; set; } = new();
    public Dictionary<string, double> PhaseSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["metadata"] = 0.8, ["transcripts"] = 1.0, ["subtitle-download"] = 0.8, ["subtitle-format"] = 0.1,
        ["subtitle-save"] = 0.1, ["video-download"] = 10.0, ["video-postprocess"] = 4.0, ["audio-download"] = 5.0,
        ["audio-convert"] = 3.0, ["media-finalize"] = 0.5,
    };
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public static string AppDirectory { get { var path = Path.Combine(AppContext.BaseDirectory, "config"); Directory.CreateDirectory(path); return path; } }
    public static string ConfigPath => Path.Combine(AppDirectory, "config.json");
    private static string LegacyBrollConfigPath => Path.Combine(AppDirectory, "broll.json");

    public static AppConfig Load()
    {
        AppConfig config;
        var hasBrollSection = false;
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                using var document = JsonDocument.Parse(json);
                hasBrollSection = document.RootElement.TryGetProperty("broll", out _);
                config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            else config = new AppConfig();
        }
        catch { config = new AppConfig(); }

        var migratedLegacyBroll = false;
        if (!hasBrollSection && File.Exists(LegacyBrollConfigPath))
        {
            try
            {
                var legacy = JsonSerializer.Deserialize<BrollSettings>(File.ReadAllText(LegacyBrollConfigPath), JsonOptions);
                if (legacy is not null)
                {
                    config.Broll = legacy;
                    migratedLegacyBroll = true;
                }
            }
            catch { }
        }

        config.Normalize();
        if (migratedLegacyBroll && config.SaveCore())
        {
            try { File.Delete(LegacyBrollConfigPath); }
            catch { }
        }
        return config;
    }

    public void Save() { Normalize(); _ = SaveCore(); }

    private bool SaveCore()
    {
        var temp = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, ConfigPath, true);
            return true;
        }
        catch { return false; }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    private void Normalize()
    {
        Logging = Logging.Trim().ToLowerInvariant(); if (Logging is not ("off" or "single" or "all")) Logging = "single";
        if (LastFormat is not ("srt" or "sub" or "txt" or "vtt")) LastFormat = "srt";
        EditingRoot = EditingRoot.Trim();
        ClipNameMaxWords = Math.Clamp(ClipNameMaxWords, 1, 12);
        if (string.IsNullOrWhiteSpace(AvMarkerHtml) || !AvMarkerHtml.Contains("{id}", StringComparison.Ordinal)) AvMarkerHtml = "VLC AV {id}";
        if (string.IsNullOrWhiteSpace(BrollMarkerHtml) || !BrollMarkerHtml.Contains("{id}", StringComparison.Ordinal)) BrollMarkerHtml = "VLC LOOP {id}";
        Broll ??= new BrollSettings();
        Broll.Normalize();
        var defaults = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["metadata"] = 0.8, ["transcripts"] = 1.0, ["subtitle-download"] = 0.8, ["subtitle-format"] = 0.1,
            ["subtitle-save"] = 0.1, ["video-download"] = 10.0, ["video-postprocess"] = 4.0, ["audio-download"] = 5.0,
            ["audio-convert"] = 3.0, ["media-finalize"] = 0.5,
        };
        foreach (var item in defaults) PhaseSeconds.TryAdd(item.Key, item.Value);
        foreach (var key in PhaseSeconds.Keys.ToList()) if (!double.IsFinite(PhaseSeconds[key]) || PhaseSeconds[key] <= 0) PhaseSeconds[key] = defaults.GetValueOrDefault(key, 0.5);
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

internal static class AppLog
{
    private static string _mode = "off";
    private static readonly object Sync = new();
    private static readonly Stopwatch Runtime = Stopwatch.StartNew();
    private static readonly UTF8Encoding Utf8 = new(false);
    private static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");
    public static string LogPath => Path.Combine(LogDirectory, "YouTubeSubs.log");
    public static bool Enabled => _mode != "off";
    public static void Initialize(string mode)
    {
        _mode = mode is "single" or "all" ? mode : "off"; if (_mode == "off") return;
        try
        {
            Directory.CreateDirectory(LogDirectory); if (_mode == "single") File.WriteAllText(LogPath, string.Empty, Utf8);
            Write("SESSION START", $"pid={Environment.ProcessId} mode={_mode}");
            Write("SESSION", $"executable={Environment.ProcessPath}"); Write("SESSION", $"working_directory={Environment.CurrentDirectory}");
        }
        catch { _mode = "off"; }
    }
    public static void Write(string message) => Write("INFO", message);
    public static void Write(string category, string message) { if (!Enabled) return; WriteRaw($"{DateTime.Now:dd.MM.yyyy HH:mm:ss.fff} | {category,-13} | {message}"); }
    public static void Exception(string context, Exception exception) => Write("ERROR", $"{context}: {exception.GetType().Name}: {exception.Message} | {exception.StackTrace?.Replace(Environment.NewLine, " ")}");
    public static void SessionEnd(string reason) => Write("SESSION END", $"reason={reason} runtime={Runtime.Elapsed}");
    private static void WriteRaw(string message) { lock (Sync) { try { File.AppendAllText(LogPath, message + Environment.NewLine, Utf8); } catch { } } }
}
