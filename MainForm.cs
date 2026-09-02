using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace YouTubeSubs;

internal sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly YoutubeService _service = new();
    private readonly TextBox _input = new() { Width = 390 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 72 };
    private readonly CheckBox _subtitles = new() { Text = "Subtitles", Checked = true, AutoSize = true };
    private readonly CheckBox _video = new() { Text = "Video", AutoSize = true };
    private readonly CheckBox _audio = new() { Text = "Audio", AutoSize = true };
    private readonly TextBox _from = new() { Width = 105 };
    private readonly TextBox _to = new() { Width = 105 };
    private readonly Label _range = new() { AutoSize = true };
    private readonly LinkLabel _status = new() { AutoSize = false, Width = 390, Height = 40, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button _download = new() { Text = "Download", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _analyzeTimer = new() { Interval = 500 };
    private readonly Dictionary<string, string> _languageMap = new(StringComparer.OrdinalIgnoreCase);
    private VideoInfo? _info;
    private bool _busy;

    public MainForm(AppConfig config)
    {
        _config = config; Text = $"YouTubeSubs {Program.Version}"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = true; StartPosition = FormStartPosition.CenterScreen; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(14); Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _language.Items.Add("Auto"); _language.SelectedIndex = 0; _format.Items.AddRange(new object[] { ".srt", ".sub", ".txt", ".vtt" }); var saved = "." + _config.LastFormat; _format.SelectedItem = _format.Items.Cast<object>().FirstOrDefault(i => string.Equals(i.ToString(), saved, StringComparison.OrdinalIgnoreCase)) ?? ".srt";

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 10 }; table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = "YouTube URL / Video ID", AutoSize = true }, 0, 0); table.SetColumnSpan(_input, 2); table.Controls.Add(_input, 0, 1);
        table.Controls.Add(new Label { Text = "Language", AutoSize = true }, 0, 2); _language.Dock = DockStyle.Fill; table.Controls.Add(_language, 0, 3); table.Controls.Add(_format, 1, 3);
        var outputs = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight }; outputs.Controls.Add(_subtitles); outputs.Controls.Add(_video); outputs.Controls.Add(_audio); table.SetColumnSpan(outputs, 2); table.Controls.Add(outputs, 0, 4);
        var times = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight }; times.Controls.Add(new Label { Text = "From", AutoSize = true, Margin = new Padding(0, 6, 3, 0) }); times.Controls.Add(_from); times.Controls.Add(new Label { Text = "To / length", AutoSize = true, Margin = new Padding(10, 6, 3, 0) }); times.Controls.Add(_to); table.SetColumnSpan(times, 2); table.Controls.Add(times, 0, 5);
        table.SetColumnSpan(_range, 2); table.Controls.Add(_range, 0, 6); table.SetColumnSpan(_status, 2); table.Controls.Add(_status, 0, 7);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.Right }; buttons.Controls.Add(_download); var cancel = new Button { Text = "Cancel", AutoSize = true }; buttons.Controls.Add(cancel); table.SetColumnSpan(buttons, 2); table.Controls.Add(buttons, 0, 8); Controls.Add(table);

        _input.TextChanged += (_, _) => ScheduleAnalysis(); _format.SelectedIndexChanged += (_, _) => SaveFormat(); _download.Click += async (_, _) => await DownloadAsync(); cancel.Click += (_, _) => Close();
        _subtitles.CheckedChanged += (_, _) => UpdateDownloadState(); _video.CheckedChanged += (_, _) => UpdateDownloadState(); _audio.CheckedChanged += (_, _) => UpdateDownloadState(); _from.TextChanged += (_, _) => UpdateDownloadState(); _to.TextChanged += (_, _) => UpdateDownloadState();
        _status.LinkClicked += (_, _) => { if (_info is not null) Process.Start(new ProcessStartInfo(YoutubeService.CanonicalUrl(_info.VideoId)) { UseShellExecute = true }); };
        _analyzeTimer.Tick += async (_, _) => { _analyzeTimer.Stop(); await AnalyzeAsync(); }; Shown += (_, _) => { _input.Focus(); ActivateFront(); };
    }

    public void ActivateFront() { if (IsDisposed) return; if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Show(); TopMost = true; BringToFront(); Activate(); BeginInvoke(new Action(async () => { await Task.Delay(200); if (!IsDisposed) TopMost = false; })); }
    private void ScheduleAnalysis() { if (_busy) return; _analyzeTimer.Stop(); ClearState(false); if (!string.IsNullOrWhiteSpace(_input.Text)) _analyzeTimer.Start(); }
    private void ClearState(bool invalid) { _info = null; _languageMap.Clear(); _language.Items.Clear(); _language.Items.Add("Auto"); _language.SelectedIndex = 0; _download.Enabled = false; _range.Text = ""; _status.Text = invalid ? "Invalid Video ID. Please try again..." : string.Empty; _status.LinkColor = invalid ? Color.FromArgb(192, 0, 0) : Color.FromArgb(5, 99, 193); _status.Links.Clear(); }

    private Task AnalyzeAsync()
    {
        var value = _input.Text.Trim(); if (_busy || string.IsNullOrWhiteSpace(value)) return Task.CompletedTask;
        try { _ = YoutubeService.ExtractVideoId(value); } catch { ClearState(true); return Task.CompletedTask; }
        _busy = true; ClearState(false); using var dialog = new ProgressDialog(this, "Analyzing video", new[] { "metadata", "transcripts" }, _config); VideoInfo? result = null; Exception? error = null;
        dialog.Shown += async (_, _) => { try { result = await _service.AnalyzeAsync(value, dialog.SetPhase, dialog.Cancellation.Token); } catch (OperationCanceledException) { } catch (Exception ex) { error = ex; } finally { dialog.Finish(error is null && !dialog.Cancellation.IsCancellationRequested); } };
        dialog.ShowDialog(this); _busy = false; if (dialog.ExitApplication) { Application.Exit(); return Task.CompletedTask; } if (dialog.Cancellation.IsCancellationRequested) { ClearState(false); return Task.CompletedTask; } if (error is not null || result is null) { ClearState(true); ActivateFront(); return Task.CompletedTask; }
        _info = result; _language.Items.Clear(); _language.Items.Add("Auto"); foreach (var (label, code) in result.LanguageChoices()) { _language.Items.Add(label); _languageMap[label] = code; } _language.SelectedIndex = 0;
        _subtitles.Enabled = result.Tracks.Count > 0; if (result.Tracks.Count == 0) _subtitles.Checked = false;
        var timestamp = YoutubeService.ExtractTimestamp(value); _from.Text = timestamp.HasValue ? FormatTime(timestamp.Value) : ""; _to.Text = "";
        _status.Text = result.Title; _status.LinkColor = Color.FromArgb(5, 99, 193); _status.Links.Clear(); _status.Links.Add(0, result.Title.Length); UpdateDownloadState(); ActivateFront(); return Task.CompletedTask;
    }

    private void UpdateDownloadState()
    {
        if (_info is null || _busy) { _download.Enabled = false; return; }
        var any = (_subtitles.Checked && _subtitles.Enabled) || _video.Checked || _audio.Checked;
        if (!TryResolveRange(out var start, out var end)) { _range.Text = "Invalid range"; _download.Enabled = false; return; }
        _range.Text = $"{FormatTime(start)} — {FormatTime(end)}  [{FormatTime(end - start)}]";
        _download.Enabled = any && (end - start).TotalSeconds >= 2;
    }

    private bool TryResolveRange(out TimeSpan start, out TimeSpan end)
    {
        start = TimeSpan.Zero; end = _info?.Duration ?? TimeSpan.Zero; if (_info is null || end <= TimeSpan.Zero) return false;
        var a = ParseTimeExpression(_from.Text, false); var b = ParseTimeExpression(_to.Text, true); if (!a.Valid || !b.Valid) return false;
        if (a.Relative && a.Sign < 0 && !b.Relative) { end = b.Empty ? end : b.Value; start = end - a.Value; }
        else { start = a.Empty ? TimeSpan.Zero : a.Value; if (b.Relative && b.Sign > 0) end = start + b.Value; else end = b.Empty ? end : b.Value; }
        start = TimeSpan.FromSeconds(Math.Clamp(start.TotalSeconds, 0, _info.Duration.TotalSeconds)); end = TimeSpan.FromSeconds(Math.Clamp(end.TotalSeconds, 0, _info.Duration.TotalSeconds));
        if (start > end) (start, end) = (end, start); return true;
    }

    private static (bool Valid, bool Empty, bool Relative, int Sign, TimeSpan Value) ParseTimeExpression(string text, bool allowPositiveRelative)
    {
        var s = text.Trim(); if (s.Length == 0) return (true, true, false, 0, TimeSpan.Zero); var relative = s[0] is '+' or '-'; var sign = relative && s[0] == '-' ? -1 : relative ? 1 : 0; if (relative) s = s[1..].Trim(); if (relative && sign > 0 && !allowPositiveRelative) return (false, false, true, sign, TimeSpan.Zero);
        if (!TryParseTime(s, out var value)) return (false, false, relative, sign, TimeSpan.Zero); return (true, false, relative, sign, value);
    }

    private static bool TryParseTime(string s, out TimeSpan value)
    {
        value = TimeSpan.Zero; if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Contains(':')) { var p = s.Split(':'); if (p.Length is < 2 or > 3 || p.Any(x => !int.TryParse(x, out _))) return false; var n = p.Select(int.Parse).ToArray(); var h = p.Length == 3 ? n[0] : 0; var m = p.Length == 3 ? n[1] : n[0]; var sec = p.Length == 3 ? n[2] : n[1]; if (m > 59 || sec > 59) return false; value = new TimeSpan(h, m, sec); return true; }
        if (!s.All(char.IsDigit) || s.Length > 6) return false; var raw = long.Parse(s, CultureInfo.InvariantCulture); long h2 = 0, m2 = 0, s2; if (s.Length <= 2) s2 = raw; else if (s.Length <= 4) { m2 = raw / 100; s2 = raw % 100; } else { h2 = raw / 10000; m2 = (raw / 100) % 100; s2 = raw % 100; } if (m2 > 59 || s2 > 59) return false; value = TimeSpan.FromSeconds(h2 * 3600 + m2 * 60 + s2); return true;
    }

    private static string FormatTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private async Task DownloadAsync()
    {
        if (_info is null || _busy || !TryResolveRange(out var start, out var end) || (end - start).TotalSeconds < 2) return;
        var outputs = new List<string>(); if (_subtitles.Checked && _subtitles.Enabled) outputs.Add("subtitles"); if (_video.Checked) outputs.Add("video"); if (_audio.Checked) outputs.Add("audio"); if (outputs.Count == 0) return;
        var ext = (_format.SelectedItem?.ToString() ?? ".srt").TrimStart('.').ToLowerInvariant(); _config.LastFormat = ext; var selected = _language.SelectedItem?.ToString() ?? "Auto"; var code = selected == "Auto" ? null : _languageMap.GetValueOrDefault(selected); var baseName = YoutubeService.CleanFilename(_info.Title);
        string directory; var paths = new Dictionary<string, string>();
        if (outputs.Count == 1)
        {
            var kind = outputs[0]; var suffix = kind == "subtitles" ? ext : kind == "video" ? "mp4" : "mp3"; using var save = new SaveFileDialog { FileName = baseName + "." + suffix, DefaultExt = suffix, AddExtension = true, Filter = $"{suffix.ToUpperInvariant()} files|*.{suffix}", OverwritePrompt = true, InitialDirectory = Directory.Exists(_config.LastOutputDirectory) ? _config.LastOutputDirectory : null }; if (DialogPositioning.ShowSaveDialogCenteredOnScreen(save, this) != DialogResult.OK) return; directory = Path.GetDirectoryName(save.FileName)!; paths[kind] = save.FileName;
        }
        else
        {
            using var folder = new FolderBrowserDialog { Description = "Select output folder", UseDescriptionForTitle = true, SelectedPath = Directory.Exists(_config.LastOutputDirectory) ? _config.LastOutputDirectory : "" }; if (folder.ShowDialog(this) != DialogResult.OK) return; directory = folder.SelectedPath; if (outputs.Contains("subtitles")) paths["subtitles"] = Path.Combine(directory, baseName + "." + ext); if (outputs.Contains("video")) paths["video"] = Path.Combine(directory, baseName + ".mp4"); if (outputs.Contains("audio")) paths["audio"] = Path.Combine(directory, baseName + ".mp3");
        }
        _config.LastOutputDirectory = directory; _config.Save(); _busy = true; _download.Enabled = false; using var dialog = new ProgressDialog(this, "Downloading", outputs.Select(x => x == "subtitles" ? "download" : x).Concat(new[] { "save" }), _config); Exception? error = null;
        dialog.Shown += async (_, _) =>
        {
            try
            {
                if (paths.TryGetValue("subtitles", out var subtitlePath)) { var text = await _service.DownloadAndFormatAsync(_info, ext, code, dialog.SetPhase, dialog.Cancellation.Token); dialog.SetPhase("save"); await File.WriteAllTextAsync(subtitlePath, text, new UTF8Encoding(false), dialog.Cancellation.Token); }
                if (paths.TryGetValue("video", out var videoPath)) { dialog.SetPhase("video"); await MediaDownloader.DownloadVideoAsync(_info.VideoId, videoPath, start, end, _info.Duration, dialog.SetProgress, dialog.Cancellation.Token); }
                if (paths.TryGetValue("audio", out var audioPath)) { dialog.SetPhase("audio"); await MediaDownloader.DownloadAudioAsync(_info.VideoId, audioPath, start, end, _info.Duration, dialog.SetProgress, dialog.Cancellation.Token); }
            }
            catch (OperationCanceledException) { foreach (var p in paths.Values) try { if (File.Exists(p)) File.Delete(p); } catch { } }
            catch (Exception ex) { error = ex; }
            finally { dialog.Finish(error is null && !dialog.Cancellation.IsCancellationRequested); }
        };
        dialog.ShowDialog(this); _busy = false; if (dialog.ExitApplication) { Application.Exit(); return; } if (dialog.Cancellation.IsCancellationRequested) { UpdateDownloadState(); return; } if (error is not null) { UpdateDownloadState(); MessageBox.Show(this, error.Message, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        var message = outputs.Count == 1 ? "File saved successfully.\n\nOpen it?" : "Files saved successfully.\n\nOpen the folder?"; var open = MessageBox.Show(this, message, "YouTubeSubs", MessageBoxButtons.YesNo, MessageBoxIcon.Information); if (open == DialogResult.Yes) { try { Process.Start(new ProcessStartInfo(outputs.Count == 1 ? paths.Values.First() : directory) { UseShellExecute = true }); } catch { } } Close();
    }

    private void SaveFormat() { var value = (_format.SelectedItem?.ToString() ?? ".srt").TrimStart('.').ToLowerInvariant(); if (value is "srt" or "sub" or "txt" or "vtt") { _config.LastFormat = value; _config.Save(); } }
}

internal static class DialogPositioning
{
    public static DialogResult ShowSaveDialogCenteredOnScreen(SaveFileDialog dialog, Form owner) { var area = Screen.FromControl(owner).WorkingArea; using var proxy = new Form { StartPosition = FormStartPosition.Manual, Bounds = area, FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, Opacity = 0, Owner = owner }; proxy.Show(); try { return dialog.ShowDialog(proxy); } finally { proxy.Close(); owner.Activate(); } }
}
