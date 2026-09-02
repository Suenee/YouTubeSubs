using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace YouTubeSubs;

internal static class MediaDownloader
{
    public static string ToolsDirectory => Path.Combine(AppContext.BaseDirectory, "tools");
    public static string YtDlpPath => Path.Combine(ToolsDirectory, "yt-dlp.exe");
    public static string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");

    public static void ValidateTools()
    {
        if (!File.Exists(YtDlpPath) || !File.Exists(FfmpegPath))
            throw new InvalidOperationException("Media tools are missing. Run upgrade.cmd to install/update yt-dlp and FFmpeg.");
    }

    public static async Task DownloadVideoAsync(string videoId, string outputPath, TimeSpan start, TimeSpan end, TimeSpan duration, Action<double>? progress, CancellationToken token)
    {
        ValidateTools();
        var args = new List<string>
        {
            "--newline", "--no-playlist", "--ffmpeg-location", ToolsDirectory,
            "-f", "bv*[height<=1080][vcodec^=avc1]+ba[ext=m4a]/b[height<=1080][ext=mp4]/bv*[height<=1080]+ba/b[height<=1080]",
            "--merge-output-format", "mp4", "-o", outputPath
        };
        AddSection(args, start, end, duration);
        args.Add(YoutubeService.CanonicalUrl(videoId));
        await RunAsync(args, progress, token);
    }

    public static async Task DownloadAudioAsync(string videoId, string outputPath, TimeSpan start, TimeSpan end, TimeSpan duration, Action<double>? progress, CancellationToken token)
    {
        ValidateTools();
        var args = new List<string>
        {
            "--newline", "--no-playlist", "--ffmpeg-location", ToolsDirectory,
            "-x", "--audio-format", "mp3", "--audio-quality", "192K", "-o", outputPath
        };
        AddSection(args, start, end, duration);
        args.Add(YoutubeService.CanonicalUrl(videoId));
        await RunAsync(args, progress, token);
    }

    private static void AddSection(List<string> args, TimeSpan start, TimeSpan end, TimeSpan duration)
    {
        if (start <= TimeSpan.Zero && end >= duration - TimeSpan.FromMilliseconds(250)) return;
        args.Add("--download-sections");
        args.Add($"*{Stamp(start)}-{Stamp(end)}");
    }

    private static string Stamp(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static async Task RunAsync(IReadOnlyList<string> args, Action<double>? progress, CancellationToken token)
    {
        var psi = new ProcessStartInfo(YtDlpPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var errors = new List<string>();
        var stdout = ReadLinesAsync(process.StandardOutput, line => ParseProgress(line, progress), token);
        var stderr = ReadLinesAsync(process.StandardError, line => { errors.Add(line); ParseProgress(line, progress); }, token);
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(token));
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(errors.LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? $"yt-dlp failed with exit code {process.ExitCode}.");
        progress?.Invoke(100);
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> lineAction, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token) is { } line) lineAction(line);
    }

    private static void ParseProgress(string line, Action<double>? progress)
    {
        var match = Regex.Match(line, @"\[download\]\s+([0-9]+(?:\.[0-9]+)?)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) progress?.Invoke(value);
    }
}
