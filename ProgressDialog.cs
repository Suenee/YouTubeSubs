namespace YouTubeSubs;

internal sealed class ProgressDialog : Form
{
    private readonly AppConfig _config;
    private readonly string[] _phases;
    private readonly Label _text = new() { AutoSize = false, Width = 410, Height = 24, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _eta = new() { AutoSize = false, Width = 410, Height = 24, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar _bar = new() { Width = 400, Height = 22, Minimum = 0, Maximum = 100, Value = 1 };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 80 };
    private string? _phase;
    private DateTime _phaseStarted;
    private double _completed;
    private bool _finishing;

    public CancellationTokenSource Cancellation { get; } = new();
    public bool ExitApplication { get; private set; }

    public ProgressDialog(Form owner, string title, IEnumerable<string> phases, AppConfig config)
    {
        _config = config;
        _phases = phases.ToArray();
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);
        Icon = owner.Icon;

        var cancel = new Button { Text = "Cancel", AutoSize = true };
        cancel.Click += (_, _) => CancelOnly();

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 4 };
        layout.Controls.Add(_text, 0, 0);
        layout.Controls.Add(_bar, 0, 1);
        layout.Controls.Add(_eta, 0, 2);
        layout.Controls.Add(cancel, 0, 3);
        layout.SetCellPosition(cancel, new TableLayoutPanelCellPosition(0, 3));
        cancel.Anchor = AnchorStyles.Right;
        Controls.Add(layout);

        _text.Text = title + "...";
        _timer.Tick += (_, _) => Animate();
        _timer.Start();
        FormClosing += OnFormClosing;
    }

    public void SetPhase(string name)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetPhase(name));
            return;
        }

        var now = DateTime.UtcNow;
        if (_phase is not null)
        {
            var duration = Math.Max(0.01, (now - _phaseStarted).TotalSeconds);
            var previous = GetAverage(_phase);
            _config.PhaseSeconds[_phase] = Math.Round(previous * 0.75 + duration * 0.25, 3);
            _completed += previous;
        }

        _phase = name;
        _phaseStarted = now;
        _text.Text = name switch
        {
            "metadata" => "Reading video information...",
            "transcripts" => "Finding available subtitles...",
            "download" => "Downloading subtitles...",
            "format" => "Creating output...",
            "save" => "Saving file...",
            _ => name,
        };
    }

    public void Finish(bool learn = true)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Finish(learn));
            return;
        }
        if (_finishing) return;
        _finishing = true;

        if (learn && _phase is not null)
        {
            var duration = Math.Max(0.01, (DateTime.UtcNow - _phaseStarted).TotalSeconds);
            var average = GetAverage(_phase);
            _config.PhaseSeconds[_phase] = Math.Round(average * 0.75 + duration * 0.25, 3);
            _config.Samples++;
            _config.Save();
        }

        _timer.Stop();
        FormClosing -= OnFormClosing;
        Close();
    }

    private double GetAverage(string phase) => _config.PhaseSeconds.TryGetValue(phase, out var value) ? value : 0.5;

    private void Animate()
    {
        if (_phase is null) return;
        var total = _phases.Sum(GetAverage);
        var average = GetAverage(_phase);
        var elapsed = (DateTime.UtcNow - _phaseStarted).TotalSeconds;
        var partial = average * Math.Min(0.92, elapsed / Math.Max(average, 0.05));
        var value = Math.Min(96, (_completed + partial) / Math.Max(total, 0.1) * 100);
        _bar.Value = Math.Clamp((int)Math.Round(value), 0, 96);

        if (_config.Samples >= 3)
        {
            var remaining = Math.Max(0, total - _completed - Math.Min(elapsed, average));
            _eta.Text = $"Estimated time remaining: ~{Math.Max(1, Math.Round(remaining))} s";
        }
    }

    private void CancelOnly()
    {
        Cancellation.Cancel();
        Finish(false);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_finishing) return;
        if (e.CloseReason == CloseReason.UserClosing)
        {
            ExitApplication = true;
            Cancellation.Cancel();
        }
    }
}
