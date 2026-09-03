using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace YouTubeSubs;

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
    public Dictionary<string, double> PhaseSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["metadata"] = 0.8, ["transcripts"] = 1.0, ["subtitle-download"] = 0.8, ["subtitle-format"] = 0.1,
        ["subtitle-save"] = 0.1, ["video-download"] = 10.0, ["video-postprocess"] = 4.0, ["audio-download"] = 5.0,
        ["audio-convert"] = 3.0, ["media-finalize"] = 0.5,
    };
    public static string AppDirectory { get { var path = Path.Combine(AppContext.BaseDirectory, "config"); Directory.CreateDirectory(path); return path; } }
    public static string ConfigPath => Path.Combine(AppDirectory, "config.json");
    public static AppConfig Load()
    {
        try { if (!File.Exists(ConfigPath)) return new AppConfig(); var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new AppConfig(); config.Normalize(); return config; }
        catch { return new AppConfig(); }
    }
    public void Save() { Normalize(); try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions)); } catch { } }
    private void Normalize()
    {
        Logging = Logging.Trim().ToLowerInvariant(); if (Logging is not ("off" or "single" or "all")) Logging = "single";
        if (LastFormat is not ("srt" or "sub" or "txt" or "vtt")) LastFormat = "srt";
        EditingRoot = EditingRoot.Trim();
        ClipNameMaxWords = Math.Clamp(ClipNameMaxWords, 1, 12);
        if (string.IsNullOrWhiteSpace(AvMarkerHtml) || !AvMarkerHtml.Contains("{id}", StringComparison.Ordinal)) AvMarkerHtml = "VLC AV {id}";
        if (string.IsNullOrWhiteSpace(BrollMarkerHtml) || !BrollMarkerHtml.Contains("{id}", StringComparison.Ordinal)) BrollMarkerHtml = "VLC LOOP {id}";
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
