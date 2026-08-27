using System.Diagnostics;
using System.Text;

namespace YouTubeSubs;

internal sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly YoutubeService _service = new();
    private readonly TextBox _input = new() { Width = 520 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 72 };
    private readonly LinkLabel _status = new() { AutoSize = false, Width = 520, Height = 40, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button _download = new() { Text = "Download", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _analyzeTimer = new() { Interval = 500 };
    private readonly Dictionary<string, string> _languageMap = new(StringComparer.OrdinalIgnoreCase);
    private VideoInfo? _info;
    private bool _busy;

    public MainForm(AppConfig config)
    {
        _config = config;
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

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 6 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = "YouTube URL / Video ID", AutoSize = true }, 0, 0);
        table.SetColumnSpan(_input, 2);
        table.Controls.Add(_input, 0, 1);
        table.Controls.Add(new Label { Text = "Language", AutoSize = true }, 0, 2);
        _language.Dock = DockStyle.Fill;
        table.Controls.Add(_language, 0, 3);
        table.Controls.Add(_format, 1, 3);
        table.SetColumnSpan(_status, 2);
        table.Controls.Add(_status, 0, 4);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.Right };
        buttons.Controls.Add(_download);
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        buttons.Controls.Add(cancel);
        table.SetColumnSpan(buttons, 2);
        table.Controls.Add(buttons, 0, 5);
        Controls.Add(table);

        _input.TextChanged += (_, _) => ScheduleAnalysis();
        _format.SelectedIndexChanged += (_, _) => SaveFormat();
        _download.Click += async (_, _) => await DownloadAsync();
        cancel.Click += (_, _) => Close();
        _status.LinkClicked += (_, _) =>
        {
            if (_info is not null)
                Process.Start(new ProcessStartInfo(YoutubeService.CanonicalUrl(_info.VideoId)) { UseShellExecute = true });
        };
        _analyzeTimer.Tick += async (_, _) =>
        {
            _analyzeTimer.Stop();
            await AnalyzeAsync();
        };
        Shown += (_, _) => { _input.Focus(); ActivateFront(); };
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

    private void ScheduleAnalysis()
    {
        if (_busy) return;
        _analyzeTimer.Stop();
        ClearState(false);
        if (!string.IsNullOrWhiteSpace(_input.Text))
            _analyzeTimer.Start();
    }

    private void ClearState(bool invalid)
    {
        _info = null;
        _languageMap.Clear();
        _language.Items.Clear();
        _language.Items.Add("Auto");
        _language.SelectedIndex = 0;
        _download.Enabled = false;
        _status.Text = invalid ? "Invalid Video ID. Please try again..." : string.Empty;
        _status.LinkColor = invalid ? Color.FromArgb(192, 0, 0) : Color.FromArgb(5, 99, 193);
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
        using var dialog = new ProgressDialog(this, "Analyzing video", new[] { "metadata", "transcripts" }, _config);
        VideoInfo? result = null;
        Exception? error = null;

        var work = Task.Run(async () =>
        {
            try { result = await _service.AnalyzeAsync(value, dialog.SetPhase, dialog.Cancellation.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex; }
            finally { dialog.Finish(error is null && !dialog.Cancellation.IsCancellationRequested); }
        });

        dialog.ShowDialog(this);
        await work;
        _busy = false;
        if (dialog.ExitApplication) { Application.Exit(); return; }
        if (dialog.Cancellation.IsCancellationRequested) { ClearState(false); return; }
        if (error is not null || result is null) { ClearState(true); ActivateFront(); return; }

        _info = result;
        var choices = result.LanguageChoices();
        _language.Items.Clear();
        _language.Items.Add("Auto");
        foreach (var (label, code) in choices)
        {
            _language.Items.Add(label);
            _languageMap[label] = code;
        }
        _language.SelectedIndex = 0;
        _status.Text = result.Title;
        _status.LinkColor = Color.FromArgb(5, 99, 193);
        _status.Links.Clear();
        _status.Links.Add(0, result.Title.Length);
        _download.Enabled = true;
        ActivateFront();
    }

    private async Task DownloadAsync()
    {
        if (_info is null || _busy) return;
        var ext = (_format.SelectedItem?.ToString() ?? ".srt").TrimStart('.').ToLowerInvariant();
        _config.LastFormat = ext;
        _config.Save();
        var selected = _language.SelectedItem?.ToString() ?? "Auto";
        var code = selected == "Auto" ? null : _languageMap.GetValueOrDefault(selected);
        var proposed = YoutubeService.CleanFilename(_info.Title) + "." + ext;

        using var save = new SaveFileDialog
        {
            FileName = proposed,
            DefaultExt = ext,
            AddExtension = true,
            Filter = $"{ext.ToUpperInvariant()} files|*.{ext}|All files|*.*",
            OverwritePrompt = true,
        };
        if (DialogPositioning.ShowSaveDialogCenteredOnScreen(save, this) != DialogResult.OK)
            return;

        _busy = true;
        _download.Enabled = false;
        using var dialog = new ProgressDialog(this, "Downloading subtitles", new[] { "download", "format", "save" }, _config);
        Exception? error = null;

        var work = Task.Run(async () =>
        {
            try
            {
                var text = await _service.DownloadAndFormatAsync(_info, ext, code, dialog.SetPhase, dialog.Cancellation.Token);
                dialog.SetPhase("save");
                await File.WriteAllTextAsync(save.FileName, text, new UTF8Encoding(false), dialog.Cancellation.Token);
                if (dialog.Cancellation.IsCancellationRequested && File.Exists(save.FileName)) File.Delete(save.FileName);
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(save.FileName)) File.Delete(save.FileName); } catch { }
            }
            catch (Exception ex) { error = ex; }
            finally { dialog.Finish(error is null && !dialog.Cancellation.IsCancellationRequested); }
        });

        dialog.ShowDialog(this);
        await work;
        _busy = false;
        if (dialog.ExitApplication) { Application.Exit(); return; }
        if (dialog.Cancellation.IsCancellationRequested) { _download.Enabled = true; return; }
        if (error is not null)
        {
            _download.Enabled = true;
            MessageBox.Show(this, error.Message, "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var open = MessageBox.Show(this, "Subtitles saved successfully.\n\nOpen the file?", "YouTubeSubs", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (open == DialogResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo(save.FileName) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, $"Unable to open the file:\n{ex.Message}", "YouTubeSubs", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        Close();
    }

    private void SaveFormat()
    {
        var value = (_format.SelectedItem?.ToString() ?? ".srt").TrimStart('.').ToLowerInvariant();
        if (value is "srt" or "sub" or "txt" or "vtt")
        {
            _config.LastFormat = value;
            _config.Save();
        }
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
        finally { proxy.Close(); owner.Activate(); }
    }
}
