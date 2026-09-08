using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YouTubeSubs;

internal static class Program
{
    public const string Version = "2.23";
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
        AppLog.Write("STARTUP", $"version={Version} elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        AppLog.Write("STARTUP", $"base_directory={AppContext.BaseDirectory}");
        AppLog.Write("STARTUP", $"config={AppConfig.ConfigPath}");
        AppLog.Write("STARTUP", $"args={string.Join(' ', args)}");

        ApplicationConfiguration.Initialize();
        AppLog.Write("STARTUP", $"ApplicationConfiguration.Initialize elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");

        try
        {
            if (globalBroll is not null) return RunGlobalBrollGui(config, globalBroll, startup);
            return RunGui(config, projectLaunch, startup);
        }
        catch (Exception ex)
        {
            AppLog.Exception("fatal application error", ex);
            MessageBox.Show(ex.Message, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 4;
        }
        finally { AppLog.SessionEnd("process exit"); }
    }

    private static int RunGlobalBrollGui(AppConfig config, GlobalBrollLaunchOptions launch, Stopwatch startup)
    {
        try
        {
            var target = GlobalBrollStorage.ResolveTargetDirectory(config.Broll);
            AppLog.Write("BROLL", $"launch mode={launch.ModeLabel} target={target} result_file={launch.ResultFile ?? "<none>"}");
            using var form = new GlobalBrollForm(config, config.Broll, launch, target);
            AppLog.Write("STARTUP", $"global BROLL form constructed elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
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
        using var listener = new UdpClient();
        try { listener.Client.Bind(new IPEndPoint(IPAddress.Loopback, GuiPort)); }
        catch (SocketException)
        {
            try
            {
                using var sender = new UdpClient();
                var payload = Encoding.UTF8.GetBytes(ProjectLaunchOptions.ToIpcMessage(projectLaunch));
                sender.Send(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, GuiPort));
            }
            catch { }
            return 0;
        }

        using var form = new MainForm(config, projectLaunch);
        AppLog.Write("STARTUP", $"MainForm constructed elapsed={startup.Elapsed.TotalMilliseconds:0.0}ms");
        UiLayoutFix.Apply(form);
        UiInteractionFix.Attach(form);
        UiDiagnostics.Attach(form, startup);
        var receiveThread = new Thread(() => ReceiveLoop(listener, form)) { IsBackground = true, Name = "YouTubeSubsActivation" };
        receiveThread.Start();
        Application.Run(form);
        return 0;
    }

    private static void ReceiveLoop(UdpClient listener, MainForm form)
    {
        while (!form.IsDisposed)
        {
            try
            {
                IPEndPoint remote = new(IPAddress.Loopback, 0);
                var bytes = listener.Receive(ref remote);
                var message = Encoding.UTF8.GetString(bytes);
                form.BeginInvoke(new Action(() =>
                {
                    if (ProjectLaunchOptions.TryParseIpcMessage(message, out var projectLaunch)) form.ApplyProjectLaunch(projectLaunch);
                    else form.ActivateFromExternalLaunch();
                }));
            }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }
            catch (Exception ex) { AppLog.Exception("activation receive", ex); }
        }
    }
}
