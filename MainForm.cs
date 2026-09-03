using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YouTubeSubs;

internal sealed class MainForm : Form
{
    private const string InputPrompt = "Enter a YouTube URL or Video ID...";
    private readonly AppConfig _config;
    private readonly YoutubeService _service = new();
    private readonly TextBox _input = new() { Width = 390 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 72 };
    private readonly CheckBox _subtitles = new() { Text = "Subtitles", Checked = true, AutoSize = true };
    private readonly CheckBox _video = new() { Text = "Video", AutoSize = true };
    private readonly CheckBox _audio = new() { Text = "Audio", AutoSize = true };
    private readonly TimeTextBox _from = new() { Width = 78, MaxLength = 9 };
    private readonly TimeTextBox _to = new() { Width = 78, MaxLength = 9 };
    private readonly Panel _fromHost;
    private readonly Panel _toHost;
    private readonly Label _durationClip = new() { AutoSize = true, ForeColor = Color.FromArgb(192, 0, 0), Margin = new Padding(0, 3, 0, 0) };
    private readonly Label _durationFull = new() { AutoSize = true, Margin = new Padding(0, 3, 0, 0) };
    private readonly LinkLabel _status = new() { AutoSize = false, Width = 390, Height = 40, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button _download = new() { Text = "Download", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _analyzeTimer = new() { Interval = 500 };
    private readonly Dictionary<string, string> _languageMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly TableLayoutPanel _projectPanel;
    private readonly Label _projectName = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly LinkLabel _projectMarker = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly TextBox _clipName = new() { Width = 300 };
    private ProjectLaunchOptions? _projectLaunch;
    private VideoInfo? _info;
    private bool _busy;
    private bool _normalizingRange;
    private bool _audioSuggestionHandled;

    public MainForm(AppConfig config, ProjectLaunchOptions? launch = null)
    {
        _config = config;
        _projectLaunch = launch;
        _fromHost = WrapTimeBox(_from);
        _toHost = WrapTimeBox(_to);

        Text = $"YouTubeSubs {Program.Version}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        _language.Items.Add("Auto");
        _language.SelectedIndex = 0;
        _format.Items.AddRange(new object[] { ".srt", ".sub", ".txt", ".vtt" });
        var saved = "." + _config.LastFormat;
        _format.SelectedItem = _format.Items.Cast<object>().FirstOrDefault(i => string.Equals(i.ToString(), saved, StringComparison.OrdinalIgnoreCase)) ?? ".srt";

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 10 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = "YouTube URL / Video ID", AutoSize = true }, 0, 0);
        table.SetColumnSpan(_input, 2);
        table.Controls.Add(_input, 0, 1);
        table.Controls.Add(new Label { Text = "Language", AutoSize = true }, 0, 2);
        _language.Dock = DockStyle.Fill;
        table.Controls.Add(_language, 0, 3);
        table.Controls.Add(_format, 1, 3);

        var outputRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var outputs = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty };
        outputs.Controls.Add(_subtitles);
        outputs.Controls.Add(_video);
        outputs.Controls.Add(_audio);
        var duration = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
        duration.Controls.Add(_durationClip);
        duration.Controls.Add(_durationFull);
        outputRow.Controls.Add(outputs, 0, 0);
        outputRow.Controls.Add(duration, 1, 0);
        table.SetColumnSpan(outputRow, 2);
        table.Controls.Add(outputRow, 0, 4);

        _projectPanel = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0, 6, 0, 3) };
        _projectPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _projectPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _projectPanel.Controls.Add(new Label { Text = "Project", AutoSize = true, Margin = new Padding(0, 3, 8, 0) }, 0, 0);
        _projectPanel.Controls.Add(_projectName, 1, 0);
        _projectPanel.Controls.Add(new Label { Text = "Marker", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 1);
        _projectMarker.Margin = new Padding(0, 3, 0, 0);
        _projectPanel.Controls.Add(_projectMarker, 1, 1);
        _projectPanel.Controls.Add(new Label { Text = "Clip name", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 2);
        _clipName.Dock = DockStyle.Fill;
        _projectPanel.Controls.Add(_clipName, 1, 2);
        table.SetColumnSpan(_projectPanel, 2);
        table.Controls.Add(_projectPanel, 0, 5);

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
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        buttons.Controls.Add(cancel);
        table.SetColumnSpan(buttons, 2);
        table.Controls.Add(buttons, 0, 8);
        Controls.Add(table);

        _input.TextChanged += (_, _) => ScheduleAnalysis();
        _format.SelectedIndexChanged += (_, _) => SaveFormat();
        _download.Click += async (_, _) => await DownloadAsync();
        cancel.Click += (_, _) => Close();
        _projectMarker.LinkClicked += (_, _) => CopyCurrentProjectMarker();
        _subtitles.CheckedChanged += (_, _) => { UpdateFormatState(); UpdateDownloadState(); };
        _video.CheckedChanged += (_, _) =>
        {
            if (_projectLaunch is null && _video.Checked && !_audioSuggestionHandled)
            {
                _audioSuggestionHandled = true;
                _audio.Checked = true;
            }
            UpdateDownloadState();
        };
        _audio.CheckedChanged += (_, _) => UpdateDownloadState();
        _from.TextChanged += (_, _) => { if (!_normalizingRange) UpdateDownloadState(); };
        _to.TextChanged += (_, _) => { if (!_normalizingRange) UpdateDownloadState(); };
        _from.Leave += (_, _) => NormalizeRange();
        _to.Leave += (_, _) => NormalizeRange();
        _status.LinkClicked += (_, _) =>
        {
            if (_info is not null) Process.Start(new ProcessStartInfo(YoutubeService.CanonicalUrl(_info.VideoId)) { UseShellExecute = true });
        };
        _analyzeTimer.Tick += async (_, _) => { _analyzeTimer.Stop(); await AnalyzeAsync(); };
        Shown += (_, _) => { _input.Focus(); ActivateFront(); };
        UpdateFormatState();
        ShowInputPrompt();
        if (_projectLaunch is not null) ApplyProjectModeState();
    }

    public void ActivateFront()
    {
        if (IsDisposed) return;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        TopMost = true;
        BringToFront();
        Activate();
        BeginInvoke(new Action(async () => { await Task.Delay(200); if (!IsDisposed) TopMost = false; }));
    }

    public void ApplyProjectLaunch(ProjectLaunchOptions launch)
    {
        if (_busy)
        {
            MessageBox.Show(this, "YouTubeSubs is busy. Finish or cancel the current operation before opening another project media ID.", "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ActivateFront();
            return;
        }

        _projectLaunch = launch;
        _audioSuggestionHandled = true;
        _analyzeTimer.Stop();
        _input.Clear();
        _clipName.Clear();
        ClearState(false);
        ApplyProjectModeState();
        AppLog.Write("PROJECT", $"active mode={launch.ModeLabel} id={launch.RequestedId} project={launch.Project}");
        ActivateFront();
        _input.Focus();
    }

    private void ApplyProjectModeState()
    {
        if (_projectLaunch is null)
        {
            _projectPanel.Visible = false;
            return;
        }

        _audioSuggestionHandled = true;
        _projectPanel.Visible = true;
        try
        {
            var projectDirectory = ProjectStorage.ResolveProjectDirectory(_config.EditingRoot, _projectLaunch.Project);
            _projectName.Text = Path.GetFileName(projectDirectory);
        }
        catch
        {
            _projectName.Text = _projectLaunch.Project;
        }
        UpdateProjectMarkerText();
        _subtitles.Checked = false;
        _subtitles.Enabled = false;
        _video.Checked = true;
        _video.Enabled = false;
        _audio.Checked = _projectLaunch.Mode == ProjectMediaMode.AudioVideo;
        _audio.Enabled = false;
        _language.Enabled = false;
        _format.Enabled = false;
        UpdateDownloadState();
    }

    private void UpdateProjectMarkerText()
    {
        if (_projectLaunch is null) { _projectMarker.Text = string.Empty; return; }
        var template = _projectLaunch.Mode == ProjectMediaMode.AudioVideo ? _config.AvMarkerHtml : _config.BrollMarkerHtml;
        try
        {
            var rendered = MarkerClipboard.Render(template, _projectLaunch.RequestedId);
            var visible = Regex.Replace(rendered, "<[^>]+>", string.Empty).Trim();
            _projectMarker.Text = string.IsNullOrWhiteSpace(visible) ? $"{_projectLaunch.ModeLabel} {_projectLaunch.RequestedId}" : visible;
        }
        catch
        {
            _projectMarker.Text = $"{_projectLaunch.ModeLabel} {_projectLaunch.RequestedId}";
        }
    }

    private void CopyCurrentProjectMarker()
    {
        if (_projectLaunch is null) return;
        var template = _projectLaunch.Mode == ProjectMediaMode.AudioVideo ? _config.AvMarkerHtml : _config.BrollMarkerHtml;
        try
        {
            MarkerClipboard.SetHtmlTemplate(template, _projectLaunch.RequestedId);
            AppLog.Write("CLIPBOARD", $"marker-click mode={_projectLaunch.ModeLabel} id={_projectLaunch.RequestedId}");
        }
        catch (Exception ex)
        {
            AppLog.Exception("marker clipboard click", ex);
            MessageBox.Show(this, ex.Message, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateFormatState() => _format.Enabled = _projectLaunch is null && _subtitles.Enabled && _subtitles.Checked;

    private void ShowInputPrompt()
    {
        _status.Text = InputPrompt;
        _status.LinkColor = SystemColors.GrayText;
        _status.Links.Clear();
    }

    private static Panel WrapTimeBox(TimeTextBox box)
    {
        box.BorderStyle = BorderStyle.None;
        box.Dock = DockStyle.Fill;
        box.Margin = Padding.Empty;
        var host = new Panel { Width = 82, Height = 23, Padding = new Padding(2, 4, 2, 2), BackColor = SystemColors.ControlDark };
        host.Controls.Add(box);
        return host;
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
        _languageMap.Clear();
        _language.Items.Clear();
        _language.Items.Add("Auto");
        _language.SelectedIndex = 0;
        _download.Enabled = false;
        _durationClip.Text = string.Empty;
        _durationFull.Text = string.Empty;
        SetTimeValidity(_from, _fromHost, true);
        SetTimeValidity(_to, _toHost, true);
        _status.Text = invalid ? "Invalid Video ID. Please try again..." : InputPrompt;
        _status.LinkColor = invalid ? Color.FromArgb(192, 0, 0) : SystemColors.GrayText;
        _status.Links.Clear();
        if (_projectLaunch is not null) ApplyProjectModeState();
    }

    private Task AnalyzeAsync()
    {
        var value = _input.Text.Trim();
        if (_busy || string.IsNullOrWhiteSpace(value)) return Task.CompletedTask;
        try { _ = YoutubeService.ExtractVideoId(value); }
        catch { ClearState(true); return Task.CompletedTask; }

        _busy = true;
        ClearState(false);
        using var dialog = new ProgressDialog(this, "Analyzing video", new[] { "metadata", "transcripts" }, _config);
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
        if (dialog.ExitApplication) { Application.Exit(); return Task.CompletedTask; }
        if (dialog.Cancellation.IsCancellationRequested) { ClearState(false); return Task.CompletedTask; }
        if (error is not null || result is null) { ClearState(true); ActivateFront(); return Task.CompletedTask; }

        _info = result;
        _language.Items.Clear();
        _language.Items.Add("Auto");
        foreach (var (label, code) in result.LanguageChoices())
        {
            _language.Items.Add(label);
            _languageMap[label] = code;
        }
        _language.SelectedIndex = 0;
        if (_projectLaunch is null)
        {
            _subtitles.Enabled = result.Tracks.Count > 0;
            if (result.Tracks.Count == 0) _subtitles.Checked = false;
            _language.Enabled = true;
            UpdateFormatState();
        }
        else
        {
            _clipName.Text = ProjectStorage.SuggestClipName(result.Title, _config.ClipNameMaxWords);
            ApplyProjectModeState();
        }

        var timestamp = YoutubeService.ExtractTimestamp(value);
        _normalizingRange = true;
        _from.Text = FormatTime(timestamp ?? TimeSpan.Zero);
        _to.Text = FormatTime(result.Duration);
        _normalizingRange = false;
        _status.Text = result.Title;
        _status.LinkColor = Color.FromArgb(5, 99, 193);
        _status.Links.Clear();
        _status.Links.Add(0, result.Title.Length);
        UpdateDownloadState();
        ActivateFront();
        return Task.CompletedTask;
    }

    private void NormalizeRange()
    {
        if (_info is null || _normalizingRange) return;
        if (!TryResolveRange(out var start, out var end, out _, out _)) { UpdateDownloadState(); return; }
        _normalizingRange = true;
        _from.Text = FormatTime(start);
        _to.Text = FormatTime(end);
        _normalizingRange = false;
        UpdateDownloadState();
    }

    private void UpdateDownloadState()
    {
        if (_info is null || _busy)
        {
            _download.Enabled = false;
            return;
        }

        var any = _projectLaunch is not null || (_subtitles.Checked && _subtitles.Enabled) || _video.Checked || _audio.Checked;
        var valid = TryResolveRange(out var start, out var end, out var invalidFrom, out var invalidTo);
        var durationOk = valid && (end - start).TotalSeconds >= 2;
        SetTimeValidity(_from, _fromHost, !invalidFrom && durationOk);
        SetTimeValidity(_to, _toHost, !invalidTo && durationOk);
        UpdateDurationIndicator(valid ? start : TimeSpan.Zero, valid ? end : _info.Duration, valid);
        _download.Enabled = any && durationOk;
    }

    private void UpdateDurationIndicator(TimeSpan start, TimeSpan end, bool valid)
    {
        if (_info is null || _info.Duration <= TimeSpan.Zero)
        {
            _durationClip.Text = string.Empty;
            _durationFull.Text = string.Empty;
            return;
        }

        var full = FormatTime(_info.Duration);
        if (!valid)
        {
            _durationClip.Text = string.Empty;
            _durationFull.Text = $"[{full}]";
            return;
        }

        var clip = end - start;
        if (IsCut(start, end))
        {
            _durationClip.Text = $"[{FormatTime(clip)}";
            _durationFull.Text = $" / {full}]";
        }
        else
        {
            _durationClip.Text = string.Empty;
            _durationFull.Text = $"[{full}]";
        }
    }

    private static void SetTimeValidity(TextBox box, Panel host, bool valid)
    {
        box.ForeColor = valid ? SystemColors.WindowText : Color.FromArgb(192, 0, 0);
        box.BackColor = valid ? SystemColors.Window : Color.FromArgb(255, 235, 235);
        host.BackColor = valid ? SystemColors.ControlDark : Color.FromArgb(192, 0, 0);
    }

    private bool TryResolveRange(out TimeSpan start, out TimeSpan end, out bool invalidFrom, out bool invalidTo)
    {
        start = TimeSpan.Zero;
        end = _info?.Duration ?? TimeSpan.Zero;
        invalidFrom = false;
        invalidTo = false;
        if (_info is null || end <= TimeSpan.Zero) return false;

        var a = ParseTimeExpression(_from.Text);
        var b = ParseTimeExpression(_to.Text);
        invalidFrom = !a.Valid;
        invalidTo = !b.Valid;
        if (!a.Valid || !b.Valid) return false;
        if (a.Relative && b.Relative)
        {
            invalidFrom = invalidTo = true;
            return false;
        }

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
        if (s.Length <= 3)
        {
            value = TimeSpan.FromSeconds(raw);
            return true;
        }

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
        value = min <= 59 && sec2 <= 59
            ? TimeSpan.FromSeconds(hh * 3600L + min * 60L + sec2)
            : TimeSpan.FromSeconds(raw);
        return true;
    }

    private static string FormatTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private bool IsCut(TimeSpan start, TimeSpan end) => _info is not null && (start > TimeSpan.FromMilliseconds(250) || end < _info.Duration - TimeSpan.FromMilliseconds(250));

    private async Task DownloadAsync()
    {
        if (_info is null || _busy) return;
        NormalizeRange();
        if (!TryResolveRange(out var start, out var end, out _, out _) || (end - start).TotalSeconds < 2) return;
        if (_projectLaunch is not null)
        {
            await DownloadProjectMediaAsync(start, end);
            return;
        }
        await DownloadNormalAsync(start, end);
    }

    private async Task DownloadProjectMediaAsync(TimeSpan start, TimeSpan end)
    {
        if (_info is null || _projectLaunch is null) return;
        var launch = _projectLaunch;
        string brollDirectory;
        try { brollDirectory = ProjectStorage.ResolveBrollDirectory(_config.EditingRoot, launch.Project); }
        catch (Exception ex)
        {
            AppLog.Exception("project directory resolution", ex);
            MessageBox.Show(this, ex.Message, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var actualId = launch.RequestedId;
        var existing = ProjectStorage.FindClipsById(brollDirectory, actualId);
        var replaceExisting = false;
        if (existing.Count > 0)
        {
            var freeId = ProjectStorage.FindNextFreeId(brollDirectory, actualId);
            using var collision = new ClipCollisionDialog(this, actualId, freeId);
            collision.ShowDialog(this);
            if (collision.Choice == ClipCollisionChoice.Cancel) return;
            if (collision.Choice == ClipCollisionChoice.Move) actualId = freeId;
            else replaceExisting = true;
        }

        var clipName = ProjectStorage.SanitizeClipName(_clipName.Text, _config.ClipNameMaxWords);
        _clipName.Text = clipName;
        var finalPath = ProjectStorage.BuildClipPath(brollDirectory, actualId, clipName);
        var temporaryPath = Path.Combine(brollDirectory, $".ytsubs-{Guid.NewGuid():N}.mp4");
        var includeAudio = launch.Mode == ProjectMediaMode.AudioVideo;
        var markerTemplate = includeAudio ? _config.AvMarkerHtml : _config.BrollMarkerHtml;
        var job = Stopwatch.StartNew();
        AppLog.Write("JOB START", $"project-media mode={launch.ModeLabel} requested_id={launch.RequestedId} final_id={actualId}");
        AppLog.Write("JOB", $"video={_info.VideoId} range={FormatTime(start)}-{FormatTime(end)} audio={includeAudio} target={finalPath}");

        _busy = true;
        _download.Enabled = false;
        using var dialog = new ProgressDialog(this, "Downloading", new[] { "video-download", "video-postprocess", "media-finalize" }, _config);
        Exception? error = null;
        dialog.Shown += async (_, _) =>
        {
            try
            {
                await MediaDownloader.DownloadVideoAsync(
                    _info.VideoId,
                    temporaryPath,
                    includeAudio,
                    start,
                    end,
                    _info.Duration,
                    dialog.SetPhase,
                    dialog.SetProgress,
                    dialog.Cancellation.Token);
                dialog.SetPhase("media-finalize");
                if (replaceExisting)
                {
                    foreach (var oldPath in existing) if (File.Exists(oldPath)) File.Delete(oldPath);
                }
                File.Move(temporaryPath, finalPath, true);
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
        if (dialog.ExitApplication) { Application.Exit(); return; }
        if (dialog.Cancellation.IsCancellationRequested)
        {
            AppLog.Write("JOB END", $"status=CANCELLED elapsed={job.Elapsed.TotalSeconds:0.00}s");
            UpdateDownloadState();
            return;
        }
        if (error is not null)
        {
            AppLog.Exception("project media download", error);
            AppLog.Write("JOB END", $"status=FAILED elapsed={job.Elapsed.TotalSeconds:0.00}s");
            UpdateDownloadState();
            MessageBox.Show(this, error.Message, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var clipboardOk = false;
        try
        {
            MarkerClipboard.SetHtmlTemplate(markerTemplate, actualId);
            var size = new FileInfo(finalPath).Length;
            AppLog.Write("OUTPUT", $"saved={finalPath} size={size}");
            AppLog.Write("CLIPBOARD", $"marker={launch.ModeLabel} id={actualId}");
            AppLog.Write("JOB END", $"status=SUCCESS elapsed={job.Elapsed.TotalSeconds:0.00}s");
            clipboardOk = true;
        }
        catch (Exception ex)
        {
            AppLog.Exception("marker clipboard", ex);
            MessageBox.Show(this, $"The media file was saved, but the marker could not be copied to Clipboard.\n\n{ex.Message}", "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (actualId != launch.RequestedId)
        {
            _projectLaunch = launch with { RequestedId = actualId };
            ApplyProjectModeState();
        }

        if (clipboardOk)
        {
            Close();
            return;
        }

        UpdateDownloadState();
        ActivateFront();
    }

    private async Task DownloadNormalAsync(TimeSpan start, TimeSpan end)
    {
        if (_info is null) return;
        var wantSubtitles = _subtitles.Checked && _subtitles.Enabled;
        var wantVideo = _video.Checked;
        var wantAudio = _audio.Checked;
        if (!wantSubtitles && !wantVideo && !wantAudio) return;

        var ext = (_format.SelectedItem?.ToString() ?? ".srt").TrimStart('.').ToLowerInvariant();
        _config.LastFormat = ext;
        var selected = _language.SelectedItem?.ToString() ?? "Auto";
        var code = selected == "Auto" ? null : _languageMap.GetValueOrDefault(selected);
        var cut = IsCut(start, end);
        var baseName = YoutubeService.CleanFilename(_info.Title) + (cut ? "-cut" : string.Empty);

        var actualKinds = new List<string>();
        if (wantSubtitles) actualKinds.Add("subtitles");
        if (wantVideo) actualKinds.Add("video");
        else if (wantAudio) actualKinds.Add("audio");

        string directory;
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (actualKinds.Count == 1)
        {
            var kind = actualKinds[0];
            var suffix = kind == "subtitles" ? ext : kind == "video" ? "mp4" : "mp3";
            using var save = new SaveFileDialog
            {
                FileName = baseName + "." + suffix,
                DefaultExt = suffix,
                AddExtension = true,
                Filter = $"{suffix.ToUpperInvariant()} files|*.{suffix}",
                OverwritePrompt = true,
                InitialDirectory = Directory.Exists(_config.LastOutputDirectory) ? _config.LastOutputDirectory : null,
            };
            if (DialogPositioning.ShowSaveDialogCenteredOnScreen(save, this) != DialogResult.OK) return;
            directory = Path.GetDirectoryName(save.FileName)!;
            paths[kind] = save.FileName;
        }
        else
        {
            using var folder = new FolderBrowserDialog
            {
                Description = "Select output folder",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(_config.LastOutputDirectory) ? _config.LastOutputDirectory : "",
            };
            if (folder.ShowDialog(this) != DialogResult.OK) return;
            directory = folder.SelectedPath;
            if (wantSubtitles) paths["subtitles"] = Path.Combine(directory, baseName + "." + ext);
            if (wantVideo) paths["video"] = Path.Combine(directory, baseName + ".mp4");
            else if (wantAudio) paths["audio"] = Path.Combine(directory, baseName + ".mp3");
        }

        _config.LastOutputDirectory = directory;
        _config.Save();
        _busy = true;
        _download.Enabled = false;

        var phases = new List<string>();
        if (wantSubtitles) phases.AddRange(new[] { "subtitle-download", "subtitle-format", "subtitle-save" });
        if (wantVideo) phases.AddRange(new[] { "video-download", "video-postprocess", "media-finalize" });
        else if (wantAudio) phases.AddRange(new[] { "audio-download", "audio-convert", "media-finalize" });

        using var dialog = new ProgressDialog(this, "Downloading", phases, _config);
        Exception? error = null;
        dialog.Shown += async (_, _) =>
        {
            try
            {
                if (paths.TryGetValue("subtitles", out var subtitlePath))
                {
                    var text = await _service.DownloadAndFormatAsync(_info, ext, code, dialog.SetPhase, dialog.Cancellation.Token, start, end);
                    dialog.SetPhase("subtitle-save");
                    await File.WriteAllTextAsync(subtitlePath, text, new UTF8Encoding(false), dialog.Cancellation.Token);
                    dialog.SetProgress(100);
                }

                if (paths.TryGetValue("video", out var videoPath))
                {
                    await MediaDownloader.DownloadVideoAsync(_info.VideoId, videoPath, wantAudio, start, end, _info.Duration, dialog.SetPhase, dialog.SetProgress, dialog.Cancellation.Token);
                    dialog.SetPhase("media-finalize");
                    dialog.SetProgress(100);
                }
                else if (paths.TryGetValue("audio", out var audioPath))
                {
                    await MediaDownloader.DownloadAudioAsync(_info.VideoId, audioPath, start, end, _info.Duration, dialog.SetPhase, dialog.SetProgress, dialog.Cancellation.Token);
                    dialog.SetPhase("media-finalize");
                    dialog.SetProgress(100);
                }
            }
            catch (OperationCanceledException)
            {
                foreach (var p in paths.Values) try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
            catch (Exception ex) { error = ex; }
            finally { dialog.Finish(error is null && !dialog.Cancellation.IsCancellationRequested); }
        };

        dialog.ShowDialog(this);
        _busy = false;
        if (dialog.ExitApplication) { Application.Exit(); return; }
        if (dialog.Cancellation.IsCancellationRequested) { UpdateDownloadState(); return; }
        if (error is not null)
        {
            UpdateDownloadState();
            MessageBox.Show(this, error.Message, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var message = actualKinds.Count == 1 ? "File saved successfully.\n\nOpen it?" : "Files saved successfully.\n\nOpen the folder?";
        var open = MessageBox.Show(this, message, "YouTubeSubs", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (open == DialogResult.Yes)
        {
            if (actualKinds.Count == 1) Process.Start(new ProcessStartInfo(paths[actualKinds[0]]) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        UpdateDownloadState();
    }

    private void SaveFormat()
    {
        var selected = _format.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selected)) return;
        _config.LastFormat = selected.TrimStart('.').ToLowerInvariant();
        _config.Save();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        TaskbarProgress.Clear(this);
        _config.Save();
        base.OnFormClosed(e);
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_from.Focused || _to.Focused)
        {
            ActiveControl = null;
            NormalizeRange();
        }
        base.OnMouseDown(e);
    }

    internal void CommitTimeEdit()
    {
        if (_from.Focused || _to.Focused) NormalizeRange();
    }
}

internal sealed class TimeTextBox : TextBox
{
    private const int WmPaste = 0x0302;

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        BeginInvoke(new Action(SelectAll));
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar)) { base.OnKeyPress(e); return; }
        if (!char.IsDigit(e.KeyChar) && e.KeyChar != ':' && e.KeyChar != '+' && e.KeyChar != '-') { e.Handled = true; return; }
        if ((e.KeyChar == '+' || e.KeyChar == '-') && !(SelectionStart == 0 && (SelectionLength == Text.Length || (Text.Length == 0 && SelectionLength == 0)))) { e.Handled = true; return; }
        base.OnKeyPress(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmPaste && Clipboard.ContainsText())
        {
            var paste = Clipboard.GetText();
            var candidate = Text.Remove(SelectionStart, SelectionLength).Insert(SelectionStart, paste);
            if (!ValidCharacters(candidate)) return;
        }
        base.WndProc(ref m);
    }

    private bool ValidCharacters(string value)
    {
        if (value.Length > MaxLength) return false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsDigit(c) || c == ':') continue;
            if ((c == '+' || c == '-') && i == 0 && value.Count(x => x is '+' or '-') == 1) continue;
            return false;
        }
        return true;
    }
}

internal static class DialogPositioning
{
    public static DialogResult ShowSaveDialogCenteredOnScreen(SaveFileDialog dialog, Form owner)
    {
        var area = Screen.FromControl(owner).WorkingArea;
        using var proxy = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Bounds = area,
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            Opacity = 0,
            Owner = owner,
        };
        proxy.Show();
        try { return dialog.ShowDialog(proxy); }
        finally { proxy.Close(); }
    }
}
