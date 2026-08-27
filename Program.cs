using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YouTubeSubs;

internal static class Program
{
    public const string Version = "2.08";
    private const int GuiPort = 45871;

    [STAThread]
    private static int Main(string[] args)
    {
        var config = AppConfig.Load();
        AppLog.Initialize(config.Logging);
        AppLog.Write($"start version={Version} mode=gui");

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
}
