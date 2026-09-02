using System.Diagnostics;
using System.Text;

namespace YouTubeSubs;

internal static class CliProgram
{
    private const string Version = "2.13";
    private static int Main(string[] args) { if (args.Length == 0) return LaunchGui(); var config = AppConfig.Load(); AppLog.Initialize(config.Logging); AppLog.Write($"start version={Version} mode=cli args={string.Join(' ', args)}"); return RunCliAsync(args).GetAwaiter().GetResult(); }
    private static int LaunchGui()
    {
        try { var guiPath = Path.Combine(AppContext.BaseDirectory, "ytsubs.exe"); if (!File.Exists(guiPath)) { Console.Error.WriteLine("ytsubs-cli: ytsubs.exe was not found next to ytsubs-cli.exe."); return 4; } Process.Start(new ProcessStartInfo { FileName = guiPath, UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory }); return 0; }
        catch (Exception ex) { Console.Error.WriteLine($"ytsubs-cli: unable to start GUI: {ex.Message}"); return 4; }
    }
    private static async Task<int> RunCliAsync(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8; if (args.Length == 1 && args[0] == "--version") { Console.Out.WriteLine($"ytsubs-cli {Version}"); return 0; }
            string? video = null; string format = "txt"; string? lang = null; string? output = null;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--format": if (++i >= args.Length) throw new ArgumentException("--format requires a value."); format = args[i].ToLowerInvariant(); if (format is not ("srt" or "sub" or "txt" or "vtt")) throw new ArgumentException("Invalid --format value."); break;
                    case "--lang": if (++i >= args.Length) throw new ArgumentException("--lang requires a value."); lang = args[i]; break;
                    case "-o": case "--output": if (++i >= args.Length) throw new ArgumentException("--output requires a value."); output = args[i]; break;
                    case "--version": Console.Out.WriteLine($"ytsubs-cli {Version}"); return 0;
                    default: if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown option '{args[i]}'."); if (video is not null) throw new ArgumentException("Only one video URL or ID may be supplied."); video = args[i]; break;
                }
            }
            if (string.IsNullOrWhiteSpace(video)) throw new ArgumentException("video is required"); var service = new YoutubeService(); var info = await service.AnalyzeAsync(video, null, CancellationToken.None); var text = await service.DownloadAndFormatAsync(info, format, lang, null, CancellationToken.None);
            if (output is not null) await File.WriteAllTextAsync(output, text, new UTF8Encoding(false)); else { Console.Out.Write(text); if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal)) Console.Out.WriteLine(); } return 0;
        }
        catch (ArgumentException ex) { Console.Error.WriteLine($"ytsubs-cli: {ex.Message}"); return 2; }
        catch (InvalidOperationException ex) when (ex.Message.Contains("subtitle", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("caption", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("language", StringComparison.OrdinalIgnoreCase)) { Console.Error.WriteLine($"ytsubs-cli: {ex.Message}"); return 3; }
        catch (IOException ex) { Console.Error.WriteLine($"ytsubs-cli: unable to write output: {ex.Message}"); return 5; }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"ytsubs-cli: unable to write output: {ex.Message}"); return 5; }
        catch (Exception ex) { Console.Error.WriteLine($"ytsubs-cli: unable to retrieve subtitles: {ex.Message}"); return 4; }
    }
}
