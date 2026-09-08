using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YouTubeSubs;

internal enum GlobalBrollMode
{
    Play,
    Loop,
}

internal sealed record GlobalBrollLaunchOptions(GlobalBrollMode Mode, string? ResultFile)
{
    public bool IncludeAudio => Mode == GlobalBrollMode.Play;
    public string ModeLabel => Mode == GlobalBrollMode.Play ? "PLAY" : "LOOP";

    public static bool IsRequested(string[] args)
        => args.Any(arg => arg.Equals("--broll:play", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--broll:loop", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--result-file", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--result-file=", StringComparison.OrdinalIgnoreCase));

    public static bool TryParse(string[] args, out GlobalBrollLaunchOptions? options, out string? error)
    {
        options = null;
        error = null;
        GlobalBrollMode? mode = null;
        string? resultFile = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--broll:play", StringComparison.OrdinalIgnoreCase))
            {
                if (mode is not null) { error = "Use either --broll:play or --broll:loop, not both."; return false; }
                mode = GlobalBrollMode.Play;
                continue;
            }
            if (arg.Equals("--broll:loop", StringComparison.OrdinalIgnoreCase))
            {
                if (mode is not null) { error = "Use either --broll:play or --broll:loop, not both."; return false; }
                mode = GlobalBrollMode.Loop;
                continue;
            }
            if (TryReadValue(args, ref i, arg, "--result-file", out var value))
            {
                if (string.IsNullOrWhiteSpace(value)) { error = "--result-file requires a path."; return false; }
                resultFile = value.Trim();
                continue;
            }

            error = $"Unknown global BROLL option '{arg}'.";
            return false;
        }

        if (mode is null) { error = "--result-file requires --broll:play or --broll:loop."; return false; }
        options = new GlobalBrollLaunchOptions(mode.Value, resultFile);
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string arg, string name, out string value)
    {
        value = string.Empty;
        if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(name.Length + 1)..];
            return true;
        }
        if (!arg.Equals(name, StringComparison.OrdinalIgnoreCase)) return false;
        if (index + 1 < args.Length) value = args[++index];
        return true;
    }
}

internal sealed class GlobalBrollConfig
{
    public List<string> Roots { get; set; } = new()
    {
        @"D:\WORK\Sueneé Universe\BROLL",
        @"N:\WORK\Sueneé Universe\BROLL",
    };
    public int MaxId { get; set; } = 9999;
    public int IdMinDigits { get; set; } = 3;
    public int ClipNameMaxWords { get; set; } = 4;
    public int UniqueNameMaxWords { get; set; } = 8;

    public static string ConfigPath => Path.Combine(AppConfig.AppDirectory, "broll.json");

    public static GlobalBrollConfig Load()
    {
        GlobalBrollConfig config;
        try
        {
            config = File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<GlobalBrollConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new GlobalBrollConfig()
                : new GlobalBrollConfig();
        }
        catch
        {
            config = new GlobalBrollConfig();
        }

        config.Normalize();
        config.Save();
        return config;
    }

    private void Normalize()
    {
        Roots = Roots.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Roots.Count == 0)
        {
            Roots.Add(@"D:\WORK\Sueneé Universe\BROLL");
            Roots.Add(@"N:\WORK\Sueneé Universe\BROLL");
        }
        MaxId = Math.Clamp(MaxId, 1, 999999);
        IdMinDigits = Math.Clamp(IdMinDigits, 1, 8);
        ClipNameMaxWords = Math.Clamp(ClipNameMaxWords, 1, 12);
        UniqueNameMaxWords = Math.Clamp(UniqueNameMaxWords, ClipNameMaxWords, 20);
    }

    private void Save()
    {
        try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(false)); }
        catch (Exception ex) { AppLog.Exception("global BROLL config save", ex); }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}

internal static class GlobalBrollStorage
{
    private static readonly Regex NumberedClip = new(@"^(?<id>\d+)\s*-\s*(?<name>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ResolveTargetDirectory(GlobalBrollConfig config)
    {
        foreach (var configured in config.Roots)
        {
            var path = Environment.ExpandEnvironmentVariables(configured);
            try
            {
                if (!Directory.Exists(path)) continue;
                _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToArray();
                return Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                AppLog.Write("BROLL", $"root unavailable path={path} reason={ex.Message}");
            }
        }
        throw new InvalidOperationException("No configured global BROLL directory is available. Check config\\broll.json.");
    }

    public static int FindFirstFreeId(string directory, int maxId)
    {
        var used = new HashSet<int>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.mp4", SearchOption.TopDirectoryOnly))
        {
            if (TryGetClipId(path, out var id) && id > 0 && id <= maxId) used.Add(id);
        }
        for (var id = 1; id <= maxId; id++) if (!used.Contains(id)) return id;
        throw new InvalidOperationException($"No free global BROLL ID is available in the configured range 1-{maxId}.");
    }

    public static string SuggestUniqueClipName(string title, string directory, GlobalBrollConfig config)
    {
        var cleaned = YoutubeService.CleanFilename(title).Trim();
        var words = Regex.Split(cleaned, @"\s+").Where(word => word.Length > 0).ToArray();
        if (words.Length == 0) return "clip";

        var existingNames = GetExistingClipNames(directory);
        var minWords = Math.Min(Math.Max(1, config.ClipNameMaxWords), words.Length);
        var maxWords = Math.Min(Math.Max(minWords, config.UniqueNameMaxWords), words.Length);
        string candidate = ProjectStorage.SanitizeClipName(string.Join(' ', words.Take(minWords)), minWords);
        for (var count = minWords; count <= maxWords; count++)
        {
            candidate = ProjectStorage.SanitizeClipName(string.Join(' ', words.Take(count)), count);
            if (!existingNames.Contains(candidate)) return candidate;
        }
        return candidate;
    }

    public static (int Id, string Path) FinalizeDownloadedFile(string temporaryPath, string directory, string clipName, GlobalBrollConfig config)
    {
        while (true)
        {
            var id = FindFirstFreeId(directory, config.MaxId);
            var finalPath = BuildClipPath(directory, id, clipName, config.IdMinDigits);
            try
            {
                File.Move(temporaryPath, finalPath, false);
                return (id, finalPath);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                AppLog.Write("BROLL", $"finalize race id={id}; retrying allocation");
            }
        }
    }

    public static string BuildClipPath(string directory, int id, string clipName, int minimumDigits)
        => Path.Combine(directory, $"{id.ToString("D" + minimumDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)} - {clipName}.mp4");

    private static HashSet<string> GetExistingClipNames(string directory)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*.mp4", SearchOption.TopDirectoryOnly))
        {
            var match = NumberedClip.Match(Path.GetFileNameWithoutExtension(path));
            if (match.Success)
            {
                var name = match.Groups["name"].Value.Trim();
                if (name.Length > 0) names.Add(name);
            }
        }
        return names;
    }

    private static bool TryGetClipId(string path, out int id)
    {
        id = 0;
        var match = NumberedClip.Match(Path.GetFileNameWithoutExtension(path));
        return match.Success && int.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }
}

internal static class GlobalBrollResult
{
    public static void Write(GlobalBrollLaunchOptions launch, string status, int? id = null, string? file = null, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(launch.ResultFile)) return;
        var target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(launch.ResultFile));
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var text = new StringBuilder()
            .AppendLine("[result]")
            .Append("status=").AppendLine(Clean(status))
            .Append("mode=").AppendLine(launch.ModeLabel.ToLowerInvariant())
            .Append("id=").AppendLine(id?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
            .Append("file=").AppendLine(Clean(file))
            .Append("message=").AppendLine(Clean(message))
            .ToString();
        try
        {
            File.WriteAllText(temp, text, Encoding.Unicode);
            File.Move(temp, target, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static string Clean(string? value)
        => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
}

internal sealed class GlobalBrollForm : Form
{
    private const string InputPrompt = "Enter a YouTube URL or Video ID...";
    private readonly AppConfig _appConfig;
    private readonly GlobalBrollConfig _brollConfig;
    private readonly GlobalBrollLaunchOptions _launch;
    private readonly string _targetDirectory;
    private readonly YoutubeService _service = new();
    private readonly TextBox _input = new() { Width = 390 };
    private readonly TextBox _clipName = new() { Width = 300 };
    private readonly TimeTextBox _from = new() { Width = 78, MaxLength = 9 };
    private readonly TimeTextBox _to = new() { Width = 78, MaxLength = 9 };
    private readonly Panel _fromHost;
    private readonly Panel _toHost;
    private readonly LinkLabel _status = new() { AutoSize = false, Width = 390, Height = 40, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _idLabel = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly Button _download = new() { Text = "Download", AutoSize = true, Enabled = false };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true };
    private readonly System.Windows.Forms.Timer _analyzeTimer = new() { Interval = 500 };
    private VideoInfo? _info;
    private bool _busy;
    private bool _normalizingRange;
    private bool _resultWritten;
    private bool _completed;

    public int ExitCode { get; private set; } = 1;

    public GlobalBrollForm(AppConfig appConfig, GlobalBrollConfig brollConfig, GlobalBrollLaunchOptions launch, string targetDirectory)
    {
        _appConfig = appConfig;
        _brollConfig = brollConfig;
        _launch = launch;
        _targetDirectory = targetDirectory;
        _fromHost = WrapTimeBox(_from);
        _toHost = WrapTimeBox(_to);

        Text = $"YouTubeSubs {Program.Version} - BROLL {launch.ModeLabel}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 10 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label { Text = "YouTube URL / Video ID", AutoSize = true }, 0, 0);
        table.SetColumnSpan(_input, 2);
        table.Controls.Add(_input, 0, 1);
        table.Controls.Add(new Label { Text = "Mode", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 2);
        table.Controls.Add(new Label { Text = launch.IncludeAudio ? "BROLL PLAY - video + audio" : "BROLL LOOP - silent video", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 3, 0, 0) }, 1, 2);
        table.Controls.Add(new Label { Text = "Target", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 3);
        table.Controls.Add(new Label { Text = targetDirectory, AutoSize = true, Margin = new Padding(0, 3, 0, 0) }, 1, 3);
        table.Controls.Add(new Label { Text = "Next ID", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 4);
        table.Controls.Add(_idLabel, 1, 4);
        table.Controls.Add(new Label { Text = "Clip name", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 5);
        _clipName.Dock = DockStyle.Fill;
        table.Controls.Add(_clipName, 1, 5);

        var times = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.None, WrapContents = false };
        times.Controls.Add(new Label { Text = "From", AutoSize = true, Margin = new Padding(0, 6, 3, 0) });
        times.Controls.Add(_fromHost);
        times.Controls.Add(new Label { Text = "To", AutoSize = true, Margin = new Padding(10, 6, 3, 0) });
        times.Controls.Add(_toHost);
        table.SetColumnSpan(times, 2);
        table.Controls.Add(times, 0, 6);

        table.SetColumnSpan(_status, 2);
        table.Controls.Add(_status, 0, 7);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.Right };
        buttons.Controls.Add(_download);
        buttons.Controls.Add(_cancel);
        table.SetColumnSpan(buttons, 2);
        table.Controls.Add(buttons, 0, 8);
        Controls.Add(table);

        _input.TextChanged += (_, _) => ScheduleAnalysis();
        _download.Click += async (_, _) => await DownloadAsync();
        _cancel.Click += (_, _) => Close();
        _from.TextChanged += (_, _) => { if (!_normalizingRange) UpdateDownloadState(); };
        _to.TextChanged += (_, _) => { if (!_normalizingRange) UpdateDownloadState(); };
        _from.Leave += (_, _) => NormalizeRange();
        _to.Leave += (_, _) => NormalizeRange();
        _status.LinkClicked += (_, _) =>
        {
            if (_info is not null) Process.Start(new ProcessStartInfo(YoutubeService.CanonicalUrl(_info.VideoId)) { UseShellExecute = true });
        };
        _analyzeTimer.Tick += async (_, _) => { _analyzeTimer.Stop(); await AnalyzeAsync(); };
        Shown += (_, _) => { RefreshId(); _input.Focus(); ActivateFront(); };
        ShowInputPrompt();
    }

    private void ActivateFront()
    {
        if (IsDisposed) return;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        TopMost = true;
        BringToFront();
        Activate();
        BeginInvoke(new Action(async () => { await Task.Delay(200); if (!IsDisposed) TopMost = false; }));
    }

    private void RefreshId()
    {
        try
        {
            var id = GlobalBrollStorage.FindFirstFreeId(_targetDirectory, _brollConfig.MaxId);
            _idLabel.Text = id.ToString("D" + _brollConfig.IdMinDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            _idLabel.Text = "unavailable";
            _status.Text = ex.Message;
            _status.LinkColor = Color.FromArgb(192, 0, 0);
            AppLog.Exception("global BROLL ID allocation", ex);
        }
    }

    private void ScheduleAnalysis()
    {
        if (_busy) return;
        _analyzeTimer.Stop();
        ClearState(false);
        if (!string.IsNullOrWhiteSpace(_input.Text)) _analyzeTimer.Start();
    }

    private void ClearState(bool invalid)
    {
        _info = null;
        _download.Enabled = false;
        _status.Text = invalid ? "Invalid Video ID. Please try again..." : InputPrompt;
        _status.LinkColor = invalid ? Color.FromArgb(192, 0, 0) : SystemColors.GrayText;
        _status.Links.Clear();
    }

    private void ShowInputPrompt()
    {
        _status.Text = InputPrompt;
        _status.LinkColor = SystemColors.GrayText;
        _status.Links.Clear();
    }

    private async Task AnalyzeAsync()
    {
        var value = _input.Text.Trim();
        if (_busy || string.IsNullOrWhiteSpace(value)) return;
        try { _ = YoutubeService.ExtractVideoId(value); }
        catch { ClearState(true); return; }

        _busy = true;
        ClearState(false);
        using var dialog = new ProgressDialog(this, "Analyzing video", new[] { "metadata", "transcripts" }, _appConfig);
        VideoInfo? result = null;
        Exception? error = null;
        dialog.Shown += async (_, _) =>
        {
            try { result = await _service.AnalyzeAsync(value, dialog.SetPhase, dialog.Cancellation.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex; }
            finally { dialog.Finish(error is null && !dialog.Cancellation.IsCancellationRequested); }
        };
        dialog.ShowDialog(this);
        _busy = false;
        if (dialog.ExitApplication) { Close(); return; }
        if (dialog.Cancellation.IsCancellationRequested) { ClearState(false); return; }
        if (error is not null || result is null) { ClearState(true); ActivateFront(); return; }

        _info = result;
        _clipName.Text = GlobalBrollStorage.SuggestUniqueClipName(result.Title, _targetDirectory, _brollConfig);
        var timestamp = YoutubeService.ExtractTimestamp(value);
        _normalizingRange = true;
        _from.Text = FormatTime(timestamp ?? TimeSpan.Zero);
        _to.Text = FormatTime(result.Duration);
        _normalizingRange = false;
        _status.Text = result.Title;
        _status.LinkColor = Color.FromArgb(5, 99, 193);
        _status.Links.Clear();
        _status.Links.Add(0, result.Title.Length);
        RefreshId();
        UpdateDownloadState();
        ActivateFront();
    }

    private async Task DownloadAsync()
    {
        if (_info is null || _busy) return;
        NormalizeRange();
        if (!TryResolveRange(out var start, out var end) || (end - start).TotalSeconds < 2) return;
        if (!Directory.Exists(_targetDirectory))
        {
            var ex = new DirectoryNotFoundException($"Global BROLL directory is no longer available: {_targetDirectory}");
            HandleDownloadError(ex);
            return;
        }

        var clipName = ProjectStorage.SanitizeClipName(_clipName.Text, _brollConfig.UniqueNameMaxWords);
        _clipName.Text = clipName;
        var temporaryPath = Path.Combine(_targetDirectory, $".ytsubs-{Guid.NewGuid():N}.mp4");
        var job = Stopwatch.StartNew();
        AppLog.Write("JOB START", $"global-broll mode={_launch.ModeLabel}");
        AppLog.Write("JOB", $"video={_info.VideoId} range={FormatTime(start)}-{FormatTime(end)} audio={_launch.IncludeAudio} target={_targetDirectory}");

        _busy = true;
        UpdateDownloadState();
        using var dialog = new ProgressDialog(this, "Downloading", new[] { "video-download", "video-postprocess", "media-finalize" }, _appConfig);
        Exception? error = null;
        (int Id, string Path)? finalized = null;
        dialog.Shown += async (_, _) =>
        {
            try
            {
                await MediaDownloader.DownloadVideoAsync(
                    _info.VideoId,
                    temporaryPath,
                    _launch.IncludeAudio,
                    start,
                    end,
                    _info.Duration,
                    dialog.SetPhase,
                    dialog.SetProgress,
                    dialog.Cancellation.Token);
                dialog.SetPhase("media-finalize");
                finalized = GlobalBrollStorage.FinalizeDownloadedFile(temporaryPath, _targetDirectory, clipName, _brollConfig);
                dialog.SetProgress(100);
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            }
            catch (Exception ex) { error = ex; }
            finally { dialog.Finish(error is null && !dialog.Cancellation.IsCancellationRequested); }
        };

        dialog.ShowDialog(this);
        _busy = false;
        try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        if (dialog.ExitApplication) { Close(); return; }
        if (dialog.Cancellation.IsCancellationRequested)
        {
            AppLog.Write("JOB END", $"status=CANCELLED elapsed={job.Elapsed.TotalSeconds:0.00}s");
            RefreshId();
            UpdateDownloadState();
            return;
        }
        if (error is not null || finalized is null)
        {
            HandleDownloadError(error ?? new InvalidOperationException("The global BROLL download did not produce an output file."));
            AppLog.Write("JOB END", $"status=FAILED elapsed={job.Elapsed.TotalSeconds:0.00}s");
            return;
        }

        var output = finalized.Value;
        AppLog.Write("OUTPUT", $"global-broll id={output.Id} saved={output.Path} size={new FileInfo(output.Path).Length}");
        AppLog.Write("JOB END", $"status=SUCCESS elapsed={job.Elapsed.TotalSeconds:0.00}s");
        try
        {
            GlobalBrollResult.Write(_launch, "success", output.Id, output.Path);
            _resultWritten = true;
            _completed = true;
            ExitCode = 0;
        }
        catch (Exception ex)
        {
            AppLog.Exception("global BROLL result write", ex);
            ExitCode = 5;
            MessageBox.Show(this, $"The media file was saved, but the result INI file could not be written.\n\n{ex.Message}", "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Close();
    }

    private void HandleDownloadError(Exception ex)
    {
        AppLog.Exception("global BROLL download", ex);
        ExitCode = 4;
        try
        {
            GlobalBrollResult.Write(_launch, "error", message: ex.Message);
            _resultWritten = true;
        }
        catch (Exception resultEx) { AppLog.Exception("global BROLL error result write", resultEx); }
        RefreshId();
        UpdateDownloadState();
        MessageBox.Show(this, ex.Message, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
        ActivateFront();
    }

    private void UpdateDownloadState()
    {
        if (_info is null || _busy) { _download.Enabled = false; return; }
        _download.Enabled = TryResolveRange(out var start, out var end) && (end - start).TotalSeconds >= 2;
    }

    private void NormalizeRange()
    {
        if (_info is null || _normalizingRange) return;
        if (!TryResolveRange(out var start, out var end)) { UpdateDownloadState(); return; }
        _normalizingRange = true;
        _from.Text = FormatTime(start);
        _to.Text = FormatTime(end);
        _normalizingRange = false;
        UpdateDownloadState();
    }

    private bool TryResolveRange(out TimeSpan start, out TimeSpan end)
    {
        start = TimeSpan.Zero;
        end = _info?.Duration ?? TimeSpan.Zero;
        if (_info is null || end <= TimeSpan.Zero) return false;

        var a = ParseTimeExpression(_from.Text);
        var b = ParseTimeExpression(_to.Text);
        if (!a.Valid || !b.Valid || (a.Relative && b.Relative)) return false;
        if (a.Relative)
        {
            var anchor = b.Empty ? _info.Duration : b.Value;
            end = anchor;
            start = a.Sign < 0 ? anchor - a.Value : anchor + a.Value;
        }
        else if (b.Relative)
        {
            var anchor = a.Empty ? TimeSpan.Zero : a.Value;
            start = anchor;
            end = b.Sign < 0 ? anchor - b.Value : anchor + b.Value;
        }
        else
        {
            start = a.Empty ? TimeSpan.Zero : a.Value;
            end = b.Empty ? _info.Duration : b.Value;
        }
        start = TimeSpan.FromSeconds(Math.Clamp(start.TotalSeconds, 0, _info.Duration.TotalSeconds));
        end = TimeSpan.FromSeconds(Math.Clamp(end.TotalSeconds, 0, _info.Duration.TotalSeconds));
        if (start > end) (start, end) = (end, start);
        return true;
    }

    private static (bool Valid, bool Empty, bool Relative, int Sign, TimeSpan Value) ParseTimeExpression(string text)
    {
        var s = text.Trim();
        if (s.Length == 0) return (true, true, false, 0, TimeSpan.Zero);
        var relative = s[0] is '+' or '-';
        var sign = relative && s[0] == '-' ? -1 : relative ? 1 : 0;
        if (relative) s = s[1..];
        if (s.Length == 0 || !TryParseTime(s, out var value)) return (false, false, relative, sign, TimeSpan.Zero);
        return (true, false, relative, sign, value);
    }

    private static bool TryParseTime(string s, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Contains(':'))
        {
            var p = s.Split(':');
            if (p.Length is < 2 or > 3 || p.Any(x => x.Length == 0 || !int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _))) return false;
            var n = p.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            var h = p.Length == 3 ? n[0] : 0;
            var m = p.Length == 3 ? n[1] : n[0];
            var sec = p.Length == 3 ? n[2] : n[1];
            if (m > 59 || sec > 59) return false;
            value = TimeSpan.FromSeconds((long)h * 3600 + m * 60 + sec);
            return true;
        }
        if (!s.All(char.IsDigit) || s.Length > 6) return false;
        var raw = long.Parse(s, CultureInfo.InvariantCulture);
        if (s.Length <= 3) { value = TimeSpan.FromSeconds(raw); return true; }
        if (s.Length == 4)
        {
            var mm = (int)(raw / 100);
            var ss = (int)(raw % 100);
            value = mm <= 59 && ss <= 59 ? TimeSpan.FromSeconds(mm * 60 + ss) : TimeSpan.FromSeconds(raw);
            return true;
        }
        var padded = s.PadLeft(6, '0');
        var hh = int.Parse(padded[..2], CultureInfo.InvariantCulture);
        var min = int.Parse(padded.Substring(2, 2), CultureInfo.InvariantCulture);
        var sec2 = int.Parse(padded.Substring(4, 2), CultureInfo.InvariantCulture);
        value = min <= 59 && sec2 <= 59 ? TimeSpan.FromSeconds(hh * 3600L + min * 60L + sec2) : TimeSpan.FromSeconds(raw);
        return true;
    }

    private static string FormatTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private static Panel WrapTimeBox(TimeTextBox box)
    {
        box.BorderStyle = BorderStyle.None;
        box.Dock = DockStyle.Fill;
        box.Margin = Padding.Empty;
        var host = new Panel { Width = 82, Height = 23, Padding = new Padding(2, 4, 2, 2), BackColor = SystemColors.ControlDark };
        host.Controls.Add(box);
        return host;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && (_from.Focused || _to.Focused))
        {
            NormalizeRange();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        TaskbarProgress.Clear(this);
        if (!_completed && !_resultWritten)
        {
            try { GlobalBrollResult.Write(_launch, "cancelled"); _resultWritten = true; }
            catch (Exception ex) { AppLog.Exception("global BROLL cancelled result write", ex); }
        }
        base.OnFormClosed(e);
    }
}
