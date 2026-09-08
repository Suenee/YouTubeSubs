using System.Diagnostics;
using System.Text;

namespace YouTubeSubs;

internal static class CliProgram
{
    private const string Version = "2.21";
    private static int Main(string[] args)
    {
        if (args.Length == 0) return LaunchGui();
        if (args.Length == 1 && args[0] == "--version") { Console.Out.WriteLine($"ytsubs-cli {Version}"); return 0; }
        var config = AppConfig.Load();
        AppLog.Initialize(config.Logging);
        AppLog.Write("CLI", $"start version={Version} args={string.Join(' ', args)}");
        return RunCliAsync(args).GetAwaiter().GetResult();
    }
    private static int LaunchGui()
    {
        try { var guiPath = Path.Combine(AppContext.BaseDirectory, "ytsubs.exe"); if (!File.Exists(guiPath)) { Console.Error.WriteLine("ytsubs-cli: ytsubs.exe was not found next to ytsubs-cli.exe."); return 4; } Process.Start(new ProcessStartInfo { FileName = guiPath, UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory }); return 0; }
        catch (Exception ex) { Console.Error.WriteLine($"ytsubs-cli: unable to start GUI: {ex.Message}"); return 4; }
    }
    private static async Task<int> RunCliAsync(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            string? video = null; string format = "txt"; string? lang = null; string? output = null;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "--format" && i + 1 < args.Length) { format = args[++i].TrimStart('.').ToLowerInvariant(); continue; }
                if (arg == "--lang" && i + 1 < args.Length) { lang = args[++i]; continue; }
                if ((arg == "-o" || arg == "--output") && i + 1 < args.Length) { output = args[++i]; continue; }
                if (arg.StartsWith('-')) { Console.Error.WriteLine($"Unknown option: {arg}"); return 2; }
                if (video is null) { video = arg; continue; }
                Console.Error.WriteLine($"Unexpected argument: {arg}"); return 2;
            }
            if (video is null) { Console.Error.WriteLine("Missing YouTube URL or Video ID."); return 2; }
            if (format is not ("srt" or "sub" or "txt" or "vtt")) { Console.Error.WriteLine("Format must be srt, sub, txt, or vtt."); return 2; }

            var service = new YoutubeService();
            var info = await service.AnalyzeAsync(video, null, CancellationToken.None);
            var chosen = service.SelectTranscript(info, lang);
            if (chosen is null) { Console.Error.WriteLine("Requested subtitles/language are not available."); return 3; }
            var text = await service.DownloadTranscriptAsync(chosen, format, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(output))
            {
                Console.Out.Write(text);
                return 0;
            }
            var full = Path.GetFullPath(output);
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(full, text, new UTF8Encoding(false));
            return 0;
        }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 3; }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine(ex.Message); return 5; }
        catch (IOException ex) { Console.Error.WriteLine(ex.Message); return 5; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 4; }
    }
}
