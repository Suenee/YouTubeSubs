using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YouTubeSubs;

internal static class Program
{
    public const string Version = "2.21";
    private const int GuiPort = 45871;

    [STAThread]
    private static int Main(string[] args)
    {
        GlobalBrollLaunchOptions? globalBroll = null;
        ProjectLaunchOptions? projectLaunch = null;
        string? argumentError;

        if (GlobalBrollLaunchOptions.IsRequested(args))
        {
            if (!GlobalBrollLaunchOptions.TryParse(args, out globalBroll, out argumentError))
            {
                MessageBox.Show(argumentError, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }
        else if (!ProjectLaunchOptions.TryParse(args, out projectLaunch, out argumentError))
        {
            MessageBox.Show(argumentError, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        var startup = Stopwatch.StartNew();
        var config = AppConfig.Load();
        AppLog.Initialize(config.Logging);
        AppLog.Write("STARTUP", $"Main entered version={Version} mode=gui elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        if (projectLaunch is not null) AppLog.Write("PROJECT", $"launch mode={projectLaunch.ModeLabel} id={projectLaunch.RequestedId} project={projectLaunch.Project}");
        if (globalBroll is not null) AppLog.Write("BROLL", $"launch mode={globalBroll.ModeLabel} result_file={globalBroll.ResultFile ?? "<none>"}");
        ApplicationConfiguration.Initialize();
        return globalBroll is not null ? RunGlobalBrollGui(config, globalBroll, startup) : RunGui(config, projectLaunch, startup);
    }

    private static int RunGlobalBrollGui(AppConfig config, GlobalBrollLaunchOptions launch, Stopwatch startup)
    {
        try
        {
            var brollConfig = GlobalBrollConfig.Load();
            var targetDirectory = GlobalBrollStorage.ResolveTargetDirectory(brollConfig);
            AppLog.Write("BROLL", $"root={targetDirectory} elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
            using var form = new GlobalBrollForm(config, brollConfig, launch, targetDirectory);
            Application.Run(form);
            return form.ExitCode;
        }
        catch (Exception ex)
        {
            AppLog.Exception("global BROLL startup", ex);
            try { GlobalBrollResult.Write(launch, "error", message: ex.Message); }
            catch (Exception resultEx) { AppLog.Exception("global BROLL startup result write", resultEx); }
            MessageBox.Show(ex.Message, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 4;
        }
    }

    private static int RunGui(AppConfig config, ProjectLaunchOptions? projectLaunch, Stopwatch startup)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            client.Client.Bind(new IPEndPoint(IPAddress.Loopback, GuiPort));
        }
        catch (SocketException)
        {
            try
            {
                using var sender = new UdpClient(AddressFamily.InterNetwork);
                var payload = Encoding.UTF8.GetBytes(projectLaunch is null ? "ACTIVATE" : projectLaunch.ToIpcMessage());
                sender.Send(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, GuiPort));
                return 0;
            }
            catch
            {
                return 4;
            }
        }

        using var form = new MainForm(config, projectLaunch);
        UiInteractionFix.Apply(form);
        UiLayoutFix.Apply(form);
        UiDiagnostics.Attach(form, config);
        var listener = Task.Run(async () =>
        {
            while (!form.IsDisposed)
            {
                try
                {
                    var result = await client.ReceiveAsync();
                    var message = Encoding.UTF8.GetString(result.Buffer);
                    if (message == "ACTIVATE")
                    {
                        if (!form.IsDisposed) form.BeginInvoke(form.ActivateFront);
                    }
                    else if (ProjectLaunchOptions.TryParseIpcMessage(message, out var incoming) && incoming is not null)
                    {
                        if (!form.IsDisposed) form.BeginInvoke(new Action(() => form.ApplyProjectLaunch(incoming)));
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (form.IsDisposed) break; }
                catch (Exception ex) { AppLog.Exception("single-instance listener", ex); }
            }
        });
        AppLog.Write("STARTUP", $"MainForm ready elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        Application.Run(form);
        client.Close();
        try { listener.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        return 0;
    }
}
