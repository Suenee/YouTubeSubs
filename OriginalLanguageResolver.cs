using System.Diagnostics;
using System.Text.Json;

namespace YouTubeSubs;

internal static class OriginalLanguageResolver
{
    public static async Task<string?> ResolveAsync(string videoId, IReadOnlyList<SubtitleTrack> tracks, CancellationToken cancellationToken)
    {
        var fromYtDlp = await TryResolveWithYtDlpAsync(videoId, cancellationToken);
        var matched = MatchAvailableCode(fromYtDlp, tracks);
        if (matched is not null) return matched;

        // A single caption language is unambiguous even if yt-dlp metadata is unavailable.
        var baseCodes = tracks.Select(t => BaseCode(t.Code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return baseCodes.Count == 1 ? tracks[0].Code : null;
    }

    private static async Task<string?> TryResolveWithYtDlpAsync(string videoId, CancellationToken cancellationToken)
    {
        var executable = FindYtDlp();
        if (executable is null) return null;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = $"--dump-single-json --skip-download --no-warnings https://www.youtube.com/watch?v={videoId}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            _ = await stderr;
            if (process.ExitCode != 0) return null;
            using var json = JsonDocument.Parse(await stdout);
            var root = json.RootElement;
            foreach (var property in new[] { "language", "default_audio_language" })
            {
                if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { AppLog.Exception("original language detection", ex); }
        return null;
    }

    private static string? FindYtDlp()
    {
        foreach (var path in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe"),
            Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"),
        }) if (File.Exists(path)) return path;
        return null;
    }

    private static string? MatchAvailableCode(string? language, IReadOnlyList<SubtitleTrack> tracks)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var wanted = BaseCode(language);
        return tracks.FirstOrDefault(t => string.Equals(BaseCode(t.Code), wanted, StringComparison.OrdinalIgnoreCase))?.Code;
    }

    internal static string BaseCode(string code)
    {
        var value = code.Trim();
        if (value.EndsWith("-orig", StringComparison.OrdinalIgnoreCase)) value = value[..^5];
        var dash = value.IndexOf('-');
        return dash > 0 ? value[..dash] : value;
    }
}
