using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace YouTubeSubs;

internal static class Program
{
    public const string Version = "2.04";
    private const int GuiPort = 45871;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
            PrepareGuiConsole();

        var config = AppConfig.Load();
        AppLog.Initialize(config.Logging);
        AppLog.Write($"start version={Version} args={string.Join(' ', args)}");

        if (args.Length > 0)
            return RunCliAsync(args).GetAwaiter().GetResult();

        ApplicationConfiguration.Initialize();
        return RunGui(config);
    }

    private static int RunGui(AppConfig config)
    {
        using var socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            socket.Client.Bind(new IPEndPoint(IPAddress.Loopback, GuiPort));
        }
        catch (SocketException)
        {
            try
            {
                using var sender = new UdpClient(AddressFamily.InterNetwork);
                sender.Send("ACTIVATE"u8.ToArray(), new IPEndPoint(IPAddress.Loopback, GuiPort));
            }
            catch { }
            return 0;
        }

        using var form = new MainForm(config);
        using var cancellation = new CancellationTokenSource();
        form.FormClosed += (_, _) => cancellation.Cancel();
        _ = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    var result = await socket.ReceiveAsync(cancellation.Token);
                    if (Encoding.ASCII.GetString(result.Buffer) == "ACTIVATE" && !form.IsDisposed)
                        form.BeginInvoke(new Action(form.ActivateFront));
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (cancellation.IsCancellationRequested) { break; }
            }
        });

        Application.Run(form);
        return 0;
    }

    private static async Task<int> RunCliAsync(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length == 1 && args[0] == "--version")
            {
                Console.Out.WriteLine($"ytsubs {Version}");
                return 0;
            }

            string? video = null;
            string format = "txt";
            string? lang = null;
            string? output = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--format":
                        if (++i >= args.Length) throw new ArgumentException("--format requires a value.");
                        format = args[i].ToLowerInvariant();
                        if (format is not ("srt" or "sub" or "txt" or "vtt")) throw new ArgumentException("Invalid --format value.");
                        break;
                    case "--lang":
                        if (++i >= args.Length) throw new ArgumentException("--lang requires a value.");
                        lang = args[i];
                        break;
                    case "-o":
                    case "--output":
                        if (++i >= args.Length) throw new ArgumentException("--output requires a value.");
                        output = args[i];
                        break;
                    case "--version":
                        Console.Out.WriteLine($"ytsubs {Version}");
                        return 0;
                    default:
                        if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown option '{args[i]}'.");
                        if (video is not null) throw new ArgumentException("Only one video URL or ID may be supplied.");
                        video = args[i];
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(video))
                throw new ArgumentException("video is required");

            var service = new YoutubeService();
            var info = await service.AnalyzeAsync(video, null, CancellationToken.None);
            var text = await service.DownloadAndFormatAsync(info, format, lang, null, CancellationToken.None);
            if (output is not null)
                await File.WriteAllTextAsync(output, text, new UTF8Encoding(false));
            else
            {
                Console.Out.Write(text);
                if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal)) Console.Out.WriteLine();
            }
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"ytsubs: {ex.Message}");
            return 2;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("subtitle", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("caption", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("language", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"ytsubs: {ex.Message}");
            return 3;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"ytsubs: unable to write output: {ex.Message}");
            return 5;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"ytsubs: unable to write output: {ex.Message}");
            return 5;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ytsubs: unable to retrieve subtitles: {ex.Message}");
            return 4;
        }
    }

    private static void PrepareGuiConsole()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var processes = new uint[4];
            var processCount = NativeMethods.GetConsoleProcessList(processes, (uint)processes.Length);
            var consoleWindow = NativeMethods.GetConsoleWindow();

            // Explorer creates a console owned only by this process for a console-subsystem EXE.
            // Hide that console immediately. When launched from an existing cmd/PowerShell window,
            // do not hide the user's console; simply detach this GUI process from it.
            if (processCount == 1 && consoleWindow != IntPtr.Zero)
                NativeMethods.ShowWindow(consoleWindow, NativeMethods.SwHide);

            NativeMethods.FreeConsole();
        }
        catch { }
    }

    private static class NativeMethods
    {
        public const int SwHide = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetConsoleProcessList([Out] uint[] processList, uint processCount);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
