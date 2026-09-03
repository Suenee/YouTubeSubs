using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YouTubeSubs;

internal static class Program
{
    public const string Version = "2.14";
    private const int GuiPort = 45871;

    [STAThread]
    private static int Main(string[] args)
    {
        var startup = Stopwatch.StartNew();
        var config = AppConfig.Load();
        AppLog.Initialize(config.Logging);
        AppLog.Write($"STARTUP Main entered version={Version} mode=gui elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");

        ApplicationConfiguration.Initialize();
        AppLog.Write($"STARTUP WinForms initialized elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");

        var result = RunGui(config, startup);
        AppLog.SessionEnd("application-exit");
        return result;
    }

    private static int RunGui(AppConfig config, Stopwatch startup)
    {
        using var socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            socket.Client.Bind(new IPEndPoint(IPAddress.Loopback, GuiPort));
            AppLog.Write($"STARTUP single-instance socket ready elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        }
        catch (SocketException)
        {
            AppLog.Write("STARTUP existing instance detected; sending ACTIVATE");
            try
            {
                using var sender = new UdpClient(AddressFamily.InterNetwork);
                sender.Send("ACTIVATE"u8.ToArray(), new IPEndPoint(IPAddress.Loopback, GuiPort));
            }
            catch (Exception ex)
            {
                AppLog.Exception("single-instance activation", ex);
            }
            return 0;
        }

        AppLog.Write($"STARTUP MainForm construction begin elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        using var form = new MainForm(config);
        AppLog.Write($"STARTUP MainForm constructed elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");

        UiLayoutFix.Apply(form);
        UiInteractionFix.Attach(form);
        UiDiagnostics.Attach(form);
        AppLog.Write($"STARTUP diagnostics attached elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");

        form.HandleCreated += (_, _) => AppLog.Write($"STARTUP window handle created elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        form.Load += (_, _) => AppLog.Write($"STARTUP form Load elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        form.Shown += (_, _) =>
        {
            AppLog.Write($"STARTUP form Shown elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
            form.BeginInvoke(new Action(() => AppLog.Write($"STARTUP first UI idle elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms")));
        };

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
                    {
                        AppLog.Write("IPC ACTIVATE received");
                        form.BeginInvoke(new Action(form.ActivateFront));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (cancellation.IsCancellationRequested) { break; }
                catch (Exception ex) { AppLog.Exception("IPC receive", ex); }
            }
        });

        AppLog.Write($"STARTUP Application.Run enter elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        Application.Run(form);
        AppLog.Write($"STARTUP Application.Run returned elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        return 0;
    }
}
