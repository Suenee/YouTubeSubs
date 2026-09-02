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

    public static async Task DownloadVideoAsync(
        string videoId,
        string outputPath,
        bool includeAudio,
        TimeSpan start,
        TimeSpan end,
        TimeSpan duration,
        Action<string>? phase,
        Action<double, string?>? progress,
        CancellationToken token)
    {
        ValidateTools();
        var partial = IsPartial(start, end, duration);
        var format = includeAudio
            ? "bv[height<=1080][vcodec^=avc1]+ba[ext=m4a]/bv[height<=1080]+ba/b[height<=1080][ext=mp4]/b[height<=1080]"
            : "bv[height<=1080][vcodec^=avc1]/bv[height<=1080]";

        if (!partial)
        {
            var fullArgs = CommonArguments();
            fullArgs.AddRange(new[] { "-f", format, "--merge-output-format", "mp4", "-o", outputPath });
            fullArgs.Add(YoutubeService.CanonicalUrl(videoId));
            await RunYtDlpAsync(fullArgs, "video-download", "video-postprocess", duration, phase, progress, token);
            return;
        }

        var clipDuration = end - start;
        var preroll = TimeSpan.FromSeconds(Math.Min(10.0, start.TotalSeconds));
        var sectionStart = start - preroll;
        var tempDirectory = Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory;
        var tempPath = Path.Combine(tempDirectory, $".ytsubs-{Guid.NewGuid():N}.mp4");

        try
        {
            var args = CommonArguments();
            args.AddRange(new[] { "-f", format, "--merge-output-format", "mp4", "-o", tempPath });
            AddSection(args, sectionStart, end, duration, forceKeyframes: true);
            args.Add(YoutubeService.CanonicalUrl(videoId));

            await RunYtDlpAsync(args, "video-download", "video-postprocess", end - sectionStart, phase, progress, token);

            phase?.Invoke("video-postprocess");
            progress?.Invoke(0, "Creating exact cut...");
            await ReencodeExactCutAsync(tempPath, outputPath, preroll, clipDuration, includeAudio, progress, token);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    public static async Task DownloadAudioAsync(
        string videoId,
        string outputPath,
        TimeSpan start,
        TimeSpan end,
        TimeSpan duration,
        Action<string>? phase,
        Action<double, string?>? progress,
        CancellationToken token)
    {
        ValidateTools();
        var args = CommonArguments();
        args.AddRange(new[] { "-f", "ba[ext=m4a]/ba", "-x", "--audio-format", "mp3", "--audio-quality", "192K", "-o", outputPath });
        AddSection(args, start, end, duration, forceKeyframes: false);
        args.Add(YoutubeService.CanonicalUrl(videoId));

        await RunYtDlpAsync(args, "audio-download", "audio-convert", end - start, phase, progress, token);
    }

    private static List<string> CommonArguments() => new()
    {
        "--newline",
        "--no-playlist",
        "--ffmpeg-location", ToolsDirectory,
        "--progress-template", "download:YTSUBS|download|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
        "--postprocessor-args", "ffmpeg:-progress pipe:2 -stats_period 0.5",
    };

    private static bool IsPartial(TimeSpan start, TimeSpan end, TimeSpan duration) =>
        start > TimeSpan.FromMilliseconds(250) || end < duration - TimeSpan.FromMilliseconds(250);

    private static void AddSection(List<string> args, TimeSpan start, TimeSpan end, TimeSpan duration, bool forceKeyframes)
    {
        if (!IsPartial(start, end, duration)) return;
        args.Add("--download-sections");
        args.Add($"*{Stamp(start)}-{Stamp(end)}");
        if (forceKeyframes) args.Add("--force-keyframes-at-cuts");
    }

    private static string Stamp(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static async Task ReencodeExactCutAsync(
        string inputPath,
        string outputPath,
        TimeSpan skip,
        TimeSpan clipDuration,
        bool includeAudio,
        Action<double, string?>? progress,
        CancellationToken token)
    {
        var psi = new ProcessStartInfo(FfmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", inputPath,
            "-ss", Stamp(skip),
            "-t", Stamp(clipDuration),
            "-map", "0:v:0",
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "18",
            "-pix_fmt", "yuv420p",
            "-force_key_frames", "0",
        }) psi.ArgumentList.Add(arg);

        if (includeAudio)
        {
            foreach (var arg in new[] { "-map", "0:a:0?", "-c:a", "aac", "-b:a", "192k" }) psi.ArgumentList.Add(arg);
        }
        else
        {
            psi.ArgumentList.Add("-an");
        }

        foreach (var arg in new[] { "-movflags", "+faststart", "-progress", "pipe:1", "-stats_period", "0.5", outputPath })
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });

        var errors = new List<string>();
        var stdout = ReadLinesAsync(process.StandardOutput, line =>
        {
            if (TryParseFfmpegTime(line, out var processed) && clipDuration.TotalMilliseconds > 0)
            {
                var percent = Math.Clamp(processed.TotalMilliseconds / clipDuration.TotalMilliseconds * 100.0, 0, 100);
                progress?.Invoke(percent, "Re-encoding exact cut...");
            }
        }, token);
        var stderr = ReadLinesAsync(process.StandardError, line => errors.Add(line), token);

        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(token));
        token.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(errors.LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? $"FFmpeg failed with exit code {process.ExitCode}.");

        progress?.Invoke(100, "Exact cut complete");
    }

    private static async Task RunYtDlpAsync(
        IReadOnlyList<string> args,
        string downloadPhase,
        string postProcessPhase,
        TimeSpan expectedDuration,
        Action<string>? phase,
        Action<double, string?>? progress,
        CancellationToken token)
    {
        var psi = new ProcessStartInfo(YtDlpPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        phase?.Invoke(downloadPhase);

        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        });

        var errors = new List<string>();
        var currentPhase = downloadPhase;
        void HandleLine(string line, bool isError)
        {
            if (isError) errors.Add(line);

            if (TryParseStructuredDownload(line, out var percent, out var detail))
            {
                if (!string.Equals(currentPhase, downloadPhase, StringComparison.OrdinalIgnoreCase))
                {
                    currentPhase = downloadPhase;
                    phase?.Invoke(currentPhase);
                }
                progress?.Invoke(percent, detail);
                return;
            }

            if (LooksLikePostProcessing(line))
            {
                if (!string.Equals(currentPhase, postProcessPhase, StringComparison.OrdinalIgnoreCase))
                {
                    currentPhase = postProcessPhase;
                    phase?.Invoke(currentPhase);
                }
            }

            if (TryParseFfmpegTime(line, out var processed) && expectedDuration.TotalMilliseconds > 0)
            {
                if (!string.Equals(currentPhase, postProcessPhase, StringComparison.OrdinalIgnoreCase))
                {
                    currentPhase = postProcessPhase;
                    phase?.Invoke(currentPhase);
                }
                var p = Math.Clamp(processed.TotalMilliseconds / expectedDuration.TotalMilliseconds * 100.0, 0, 100);
                progress?.Invoke(p, null);
                return;
            }

            if (TryParseLegacyDownload(line, out var legacy)) progress?.Invoke(legacy, null);
        }

        var stdout = ReadLinesAsync(process.StandardOutput, line => HandleLine(line, false), token);
        var stderr = ReadLinesAsync(process.StandardError, line => HandleLine(line, true), token);
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(token));
        token.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(errors.LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? $"yt-dlp failed with exit code {process.ExitCode}.");

        progress?.Invoke(100, null);
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> lineAction, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token) is { } line) lineAction(line);
    }

    private static bool TryParseStructuredDownload(string line, out double percent, out string? detail)
    {
        percent = 0;
        detail = null;
        if (!line.StartsWith("YTSUBS|download|", StringComparison.Ordinal)) return false;
        var parts = line.Split('|');
        if (parts.Length < 3) return false;
        var raw = parts[2].Replace("%", string.Empty, StringComparison.Ordinal).Trim();
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out percent)) return false;
        var extras = parts.Skip(3).Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x.Trim(), "NA", StringComparison.OrdinalIgnoreCase)).Select(x => x.Trim()).ToList();
        detail = extras.Count == 0 ? null : string.Join("   ", extras);
        return true;
    }

    private static bool TryParseLegacyDownload(string line, out double percent)
    {
        percent = 0;
        var match = Regex.Match(line, @"\[download\]\s+([0-9]+(?:\.[0-9]+)?)%");
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
    }

    private static bool LooksLikePostProcessing(string line) =>
        line.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("[Fixup", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("[VideoConvertor]", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("[VideoRemuxer]", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) && line.Contains("Destination", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseFfmpegTime(string line, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase) || line.StartsWith("out_time_us=", StringComparison.OrdinalIgnoreCase))
        {
            var value = line[(line.IndexOf('=') + 1)..].Trim();
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros)) return false;
            time = TimeSpan.FromTicks(micros * 10);
            return true;
        }
        if (line.StartsWith("out_time=", StringComparison.OrdinalIgnoreCase))
        {
            var value = line[(line.IndexOf('=') + 1)..].Trim();
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time);
        }
        return false;
    }
}
