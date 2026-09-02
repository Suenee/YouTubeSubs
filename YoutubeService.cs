using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YoutubeExplode;
using YoutubeExplode.Videos.ClosedCaptions;

namespace YouTubeSubs;

internal sealed record SubtitleTrack(string Language, string Code, bool Generated, ClosedCaptionTrackInfo Source);
internal sealed record VideoInfo(string VideoId, string Title, TimeSpan Duration, List<SubtitleTrack> Tracks, string? OriginalCode)
{
    public List<(string Label, string Code)> LanguageChoices()
    {
        var result = new List<(string Label, string Code)>();
        foreach (var group in Tracks.GroupBy(t => t.Code, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var kinds = new List<string>();
            if (group.Any(t => !t.Generated)) kinds.Add("manual");
            if (group.Any(t => t.Generated)) kinds.Add("auto");
            result.Add(($"{first.Language} ({first.Code}) — {string.Join(" + ", kinds)}", first.Code));
        }
        return result;
    }
}

internal sealed class YoutubeService
{
    private readonly YoutubeClient _youtube = new();
    private static readonly Regex VideoIdRegex = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);
    private static readonly Regex AnywhereRegex = new("(?<![A-Za-z0-9_-])([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])", RegexOptions.Compiled);

    public static string ExtractVideoId(string input)
    {
        var value = input.Trim();
        if (VideoIdRegex.IsMatch(value)) return value;
        foreach (var pattern in new[] { @"(?:[?&]v=)([A-Za-z0-9_-]{11})", @"(?:youtu\.be/)([A-Za-z0-9_-]{11})", @"(?:youtube(?:-nocookie)?\.com/(?:shorts|embed|live)/)([A-Za-z0-9_-]{11})" })
        {
            var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;
        }
        var candidates = AnywhereRegex.Matches(value).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (candidates.Count == 1) return candidates[0];
        throw new ArgumentException("Invalid YouTube URL or video ID.");
    }

    public static string CanonicalUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    public static TimeSpan? ExtractTimestamp(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)) return null;
        var values = new List<string>();
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && (pair[0].Equals("t", StringComparison.OrdinalIgnoreCase) || pair[0].Equals("start", StringComparison.OrdinalIgnoreCase)))
                values.Add(Uri.UnescapeDataString(pair[1]));
        }
        if (!string.IsNullOrWhiteSpace(uri.Fragment))
        {
            foreach (var part in uri.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0].Equals("t", StringComparison.OrdinalIgnoreCase)) values.Add(Uri.UnescapeDataString(pair[1]));
            }
        }
        foreach (var value in values)
            if (TryParseYouTubeTime(value, out var time)) return time;
        return null;
    }

    private static bool TryParseYouTubeTime(string value, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (double.TryParse(value.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
        { time = TimeSpan.FromSeconds(seconds); return true; }
        var m = Regex.Match(value, @"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$", RegexOptions.IgnoreCase);
        if (!m.Success || m.Value.Length == 0) return false;
        var h = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
        var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        var sec = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        time = new TimeSpan(h, min, sec); return true;
    }

    public static string CleanFilename(string name)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var cleaned = Regex.Replace(name, $"[{Regex.Escape(invalid)}]", "_").Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? "youtube_media" : cleaned[..Math.Min(180, cleaned.Length)];
    }

    public async Task<VideoInfo> AnalyzeAsync(string input, Action<string>? phase, CancellationToken cancellationToken)
    {
        var videoId = ExtractVideoId(input);
        phase?.Invoke("metadata");
        var video = await _youtube.Videos.GetAsync(videoId, cancellationToken);
        phase?.Invoke("transcripts");
        var tracks = new List<SubtitleTrack>();
        try
        {
            var manifest = await _youtube.Videos.ClosedCaptions.GetManifestAsync(videoId, cancellationToken);
            tracks = manifest.Tracks.Select(t => new SubtitleTrack(t.Language.Name, t.Language.Code, t.IsAutoGenerated, t)).ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        var original = tracks.FirstOrDefault(t => t.Generated)?.Code ?? tracks.FirstOrDefault()?.Code;
        return new VideoInfo(videoId, video.Title, video.Duration ?? TimeSpan.Zero, tracks, original);
    }

    public async Task<string> DownloadAndFormatAsync(VideoInfo info, string format, string? languageCode, Action<string>? phase, CancellationToken cancellationToken)
    {
        if (info.Tracks.Count == 0) throw new InvalidOperationException("This video has no available subtitles.");
        var code = languageCode ?? info.OriginalCode ?? info.Tracks[0].Code;
        var matches = info.Tracks.Where(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) throw new InvalidOperationException($"No subtitle track is available for language '{code}'.");
        var selected = matches.FirstOrDefault(t => !t.Generated) ?? matches[0];
        phase?.Invoke("download");
        var track = await _youtube.Videos.ClosedCaptions.GetAsync(selected.Source, cancellationToken);
        phase?.Invoke("format");
        return Format(track, format);
    }

    private static string Format(ClosedCaptionTrack track, string format) => format switch
    {
        "txt" => FormatTxt(track), "srt" => FormatSrt(track), "vtt" => FormatVtt(track), "sub" => FormatSub(track), _ => throw new ArgumentException($"Unsupported format '{format}'.")
    };

    private static string FormatTxt(ClosedCaptionTrack track)
    {
        var output = new List<string>();
        foreach (var caption in track.Captions)
        {
            var lines = Regex.Split(caption.Text.Replace("\r\n", "\n").Replace('\r', '\n'), "\n").Select(line => line.TrimEnd()).ToList();
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
            var previousBlank = false;
            foreach (var line in lines)
            {
                var blank = string.IsNullOrWhiteSpace(line);
                if (blank) { if (!previousBlank && output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1])) output.Add(string.Empty); }
                else output.Add(line);
                previousBlank = blank;
            }
        }
        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[^1])) output.RemoveAt(output.Count - 1);
        return string.Join(Environment.NewLine, output) + (output.Count > 0 ? Environment.NewLine : string.Empty);
    }

    private static string FormatSrt(ClosedCaptionTrack track)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < track.Captions.Count; i++) { var c = track.Captions[i]; sb.AppendLine((i + 1).ToString(CultureInfo.InvariantCulture)); sb.Append(Stamp(c.Offset, false, true)).Append(" --> ").AppendLine(Stamp(c.Offset + c.Duration, false, true)); sb.AppendLine(c.Text).AppendLine(); }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
    private static string FormatVtt(ClosedCaptionTrack track)
    {
        var sb = new StringBuilder("WEBVTT").AppendLine().AppendLine(); foreach (var c in track.Captions) { sb.Append(Stamp(c.Offset, true, false)).Append(" --> ").AppendLine(Stamp(c.Offset + c.Duration, true, false)); sb.AppendLine(c.Text).AppendLine(); } return sb.ToString();
    }
    private static string FormatSub(ClosedCaptionTrack track)
    {
        var sb = new StringBuilder(); foreach (var c in track.Captions) { sb.Append(Stamp(c.Offset, false, false)).Append(',').AppendLine(Stamp(c.Offset + c.Duration, false, false)); sb.AppendLine(c.Text.Replace(Environment.NewLine, "[br]")).AppendLine(); } return sb.ToString();
    }
    private static string Stamp(TimeSpan time, bool vtt, bool srt)
    {
        var hours = (int)Math.Floor(time.TotalHours); var sep = vtt ? '.' : srt ? ',' : ':'; return $"{hours:00}:{time.Minutes:00}:{time.Seconds:00}{sep}{time.Milliseconds:000}";
    }
}
