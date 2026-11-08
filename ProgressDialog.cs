namespace YouTubeSubs;

internal sealed class ProgressDialog : Form
{
    private readonly AppConfig _config;
    private readonly Form _owner;
    private readonly List<string> _phases;
    private readonly Label _text = new() { AutoSize = false, Height = 24, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar _stageBar = new() { Height = 22, Minimum = 0, Maximum = 100, Value = 0, Dock = DockStyle.Fill };
    private readonly Label _overallText = new() { AutoSize = false, Height = 22, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar _overallBar = new() { Height = 18, Minimum = 0, Maximum = 100, Value = 0, Dock = DockStyle.Fill };
    private readonly Label _eta = new() { AutoSize = false, Height = 24, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private string? _phase;
    private DateTime _phaseStarted;
    private double _completedWeight;
    private bool _finishing;
    private double? _explicitProgress;
    private string? _detail;
    private DateTime _lastProgressUpdate;
    private double _lastReportedProgress = -1;

    public CancellationTokenSource Cancellation { get; } = new();
    public bool ExitApplication { get; private set; }

    public ProgressDialog(Form owner, string title, IEnumerable<string> phases, AppConfig config)
    {
        _config = config;
        _owner = owner;
        _phases = phases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(14); Icon = owner.Icon;
        var cancel = new Button { Text = "Cancel", AutoSize = true, Anchor = AnchorStyles.Right };
        cancel.Click += (_, _) => CancelOnly();
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 6, Width = 440 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(_text, 0, 0); layout.Controls.Add(_stageBar, 0, 1); layout.Controls.Add(_overallText, 0, 2);
        layout.Controls.Add(_overallBar, 0, 3); layout.Controls.Add(_eta, 0, 4); layout.Controls.Add(cancel, 0, 5); Controls.Add(layout);
        _text.Text = title + "..."; _overallText.Text = "Overall progress  0%";
        _timer.Tick += (_, _) => Animate(); _timer.Start(); FormClosing += OnFormClosing;
        TaskbarProgress.SetProgress(_owner, 0);
        AppLog.Write("DIALOG START", $"title={title} phases={string.Join(',', _phases)}");
    }

    public void SetPhase(string name)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetPhase(name))); return; }
        if (_finishing || string.Equals(_phase, name, StringComparison.OrdinalIgnoreCase)) return;
        CompletePreviousPhase();
        if (!_phases.Contains(name, StringComparer.OrdinalIgnoreCase)) _phases.Add(name);
        _phase = name; _phaseStarted = DateTime.UtcNow; _explicitProgress = null; _detail = null; _lastReportedProgress = -1;
        _stageBar.Style = ProgressBarStyle.Marquee; _stageBar.MarqueeAnimationSpeed = 28; _text.Text = PhaseText(name); UpdateOverall();
        AppLog.Write("PHASE START", name);
    }

    public void SetProgress(double percent, string? detail = null)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetProgress(percent, detail))); return; }
        if (_finishing) return;
        _explicitProgress = Math.Clamp(percent, 0, 100); _detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        _stageBar.Style = ProgressBarStyle.Blocks; _stageBar.Value = Math.Clamp((int)Math.Round(_explicitProgress.Value), 0, 100);
        _text.Text = $"{PhaseText(_phase)}  {_explicitProgress.Value:0.0}%" + (_detail is null ? string.Empty : $"   {_detail}");
        if ((DateTime.UtcNow - _lastProgressUpdate).TotalSeconds >= 15 || Math.Abs(_explicitProgress.Value - _lastReportedProgress) >= 10)
        {
            AppLog.Write("UI PROGRESS", $"phase={_phase} percent={_explicitProgress.Value:0.0} detail={_detail ?? "none"}");
            _lastProgressUpdate = DateTime.UtcNow;
            _lastReportedProgress = _explicitProgress.Value;
        }
        UpdateOverall();
    }

    public void Finish(bool learn = true)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => Finish(learn))); return; }
        if (_finishing) return;
        if (_phase is not null)
        {
            var elapsed = DateTime.UtcNow - _phaseStarted;
            if (learn) LearnPhase(_phase, elapsed);
            AppLog.Write("PHASE END", $"{_phase} elapsed={elapsed.TotalSeconds:0.000}s status={(learn ? "OK" : "CANCELLED")}");
        }
        _finishing = true; _timer.Stop();
        _stageBar.Style = ProgressBarStyle.Blocks;
        _stageBar.Value = learn ? 100 : 0;
        _overallBar.Style = ProgressBarStyle.Blocks;
        _overallBar.Value = learn ? 100 : 0;
        _overallText.Text = learn ? "Overall progress  100%" : "Cancelled";
        if (learn) { _config.Samples++; _config.Save(); TaskbarProgress.SetProgress(_owner, 100); }
        else TaskbarProgress.Clear(_owner);
        AppLog.Write("DIALOG END", $"title={Text} status={(learn ? "SUCCESS" : "CANCELLED")}");
        FormClosing -= OnFormClosing; Close();
        TaskbarProgress.Clear(_owner);
    }

    private void CompletePreviousPhase()
    {
        if (_phase is null) return;
        var elapsed = DateTime.UtcNow - _phaseStarted;
        var oldAverage = GetAverage(_phase); LearnPhase(_phase, elapsed); _completedWeight += oldAverage;
        AppLog.Write("PHASE END", $"{_phase} elapsed={elapsed.TotalSeconds:0.000}s status=OK");
    }

    private void LearnPhase(string phase, TimeSpan elapsed)
    {
        var seconds = Math.Max(0.01, elapsed.TotalSeconds); var previous = GetAverage(phase); var alpha = _config.Samples < 3 ? 0.45 : 0.22;
        _config.PhaseSeconds[phase] = Math.Round(previous * (1 - alpha) + seconds * alpha, 3);
    }

    private double GetAverage(string phase) => _config.PhaseSeconds.TryGetValue(phase, out var value) && value > 0 ? value : 0.5;
    private void Animate() { if (_finishing) return; if (_phase is not null && !_explicitProgress.HasValue) _text.Text = PhaseText(_phase); UpdateOverall(); }
    private void UpdateOverall()
    {
        if (_phases.Count == 0) return;
        // Never infer transfer progress from elapsed wall-clock time.
        if (!_explicitProgress.HasValue)
        {
            _overallBar.Style = ProgressBarStyle.Marquee;
            _overallBar.MarqueeAnimationSpeed = 28;
            _overallText.Text = "Overall progress: waiting for measurable data...";
            _eta.Text = "Working; no transfer percentage available yet";
            TaskbarProgress.Clear(_owner);
            return;
        }
        var total = Math.Max(0.1, _phases.Sum(GetAverage));
        var weight = GetAverage(_phase ?? string.Empty);
        var estimate = Math.Clamp((_completedWeight + weight * (_explicitProgress.Value / 100.0)) / total * 100.0, 0, 99.5);
        _overallBar.Style = ProgressBarStyle.Blocks;
        _overallBar.Value = Math.Clamp((int)Math.Round(estimate), 0, 99);
        _overallText.Text = $"Estimated overall progress  {estimate:0}%";
        _eta.Text = "Current stage measured; overall estimate uses historical phase weights";
        TaskbarProgress.SetProgress(_owner, estimate);
    }

    private static string PhaseText(string? name) => name switch
    {
        "metadata" => "Reading video information...", "transcripts" => "Finding available subtitles...",
        "subtitle-download" => "Downloading subtitles...", "subtitle-format" => "Creating subtitle output...", "subtitle-save" => "Saving subtitles...",
        "video-download" => "Downloading video...", "video-postprocess" => "Processing video...", "audio-download" => "Downloading audio...",
        "audio-convert" => "Converting to MP3...", "media-finalize" => "Finalizing output...", null => "Preparing...", _ => name.Replace('-', ' ') + "...",
    };
    private static string FormatEta(double seconds) { var time = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds))); return time.TotalHours >= 1 ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes:00}:{time.Seconds:00}"; }
    private void CancelOnly() { AppLog.Write("CANCEL", $"user requested cancellation phase={_phase ?? "none"}"); Cancellation.Cancel(); Finish(false); }
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_finishing) return;
        if (e.CloseReason == CloseReason.UserClosing) { ExitApplication = true; AppLog.Write("CANCEL", $"dialog closed by user phase={_phase ?? "none"}"); Cancellation.Cancel(); TaskbarProgress.Clear(_owner); }
    }
}