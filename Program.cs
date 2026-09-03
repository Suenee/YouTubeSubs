using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YouTubeSubs;

internal static class Program
{
    public const string Version = "2.17";
    private const int GuiPort = 45871;

    [STAThread]
    private static int Main(string[] args)
    {
        if (!ProjectLaunchOptions.TryParse(args, out var launch, out var argumentError))
        {
            MessageBox.Show(argumentError, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        var startup = Stopwatch.StartNew();
        var config = AppConfig.Load();
        AppLog.Initialize(config.Logging);
        AppLog.Write("STARTUP", $"Main entered version={Version} mode=gui elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        if (launch is not null) AppLog.Write("PROJECT", $"launch mode={launch.ModeLabel} id={launch.RequestedId} project={launch.Project}");
        ApplicationConfiguration.Initialize();
        AppLog.Write("STARTUP", $"WinForms initialized elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        var result = RunGui(config, startup, launch);
        AppLog.SessionEnd("application-exit");
        return result;
    }

    private static int RunGui(AppConfig config, Stopwatch startup, ProjectLaunchOptions? launch)
    {
        using var socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            socket.Client.Bind(new IPEndPoint(IPAddress.Loopback, GuiPort));
            AppLog.Write("STARTUP", $"single-instance socket ready elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        }
        catch (SocketException)
        {
            AppLog.Write("STARTUP", "existing instance detected; forwarding activation");
            try
            {
                using var sender = new UdpClient(AddressFamily.InterNetwork);
                var message = launch?.ToIpcMessage() ?? "ACTIVATE";
                sender.Send(Encoding.UTF8.GetBytes(message), new IPEndPoint(IPAddress.Loopback, GuiPort));
            }
            catch (Exception ex) { AppLog.Exception("single-instance activation", ex); }
            return 0;
        }

        AppLog.Write("STARTUP", $"MainForm construction begin elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        using var form = new MainForm(config, launch);
        AppLog.Write("STARTUP", $"MainForm constructed elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        UiLayoutFix.Apply(form);
        UiInteractionFix.Attach(form);
        UiDiagnostics.Attach(form);
        AppLog.Write("STARTUP", $"diagnostics attached elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        form.HandleCreated += (_, _) => AppLog.Write("STARTUP", $"window handle created elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        form.Load += (_, _) => AppLog.Write("STARTUP", $"form Load elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        form.Shown += (_, _) =>
        {
            AppLog.Write("STARTUP", $"form Shown elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
            form.BeginInvoke(new Action(() => AppLog.Write("STARTUP", $"first UI idle elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms")));
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
                    var message = Encoding.UTF8.GetString(result.Buffer);
                    if (message == "ACTIVATE" && !form.IsDisposed)
                    {
                        AppLog.Write("IPC", "ACTIVATE received");
                        form.BeginInvoke(new Action(form.ActivateFront));
                    }
                    else if (ProjectLaunchOptions.TryFromIpcMessage(message, out var forwarded) && forwarded is not null && !form.IsDisposed)
                    {
                        AppLog.Write("IPC", $"project launch received mode={forwarded.ModeLabel} id={forwarded.RequestedId} project={forwarded.Project}");
                        form.BeginInvoke(new Action(() => form.ApplyProjectLaunch(forwarded)));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (cancellation.IsCancellationRequested) { break; }
                catch (Exception ex) { AppLog.Exception("IPC receive", ex); }
            }
        });
        AppLog.Write("STARTUP", $"Application.Run enter elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        Application.Run(form);
        AppLog.Write("STARTUP", $"Application.Run returned elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        return 0;
    }
}
