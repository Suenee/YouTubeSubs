using System.Diagnostics;
using System.Reflection;

namespace YouTubeSubs;

internal static class UiDiagnostics
{
    public static void Attach(Form form)
    {
        if (!AppLog.Enabled) return;
        AttachRecursive(form);
        form.Activated += (_, _) => AppLog.Write("UI form activated");
        form.Deactivate += (_, _) => AppLog.Write("UI form deactivated");
        form.FormClosing += (_, e) => AppLog.Write($"UI form closing reason={e.CloseReason}");
        form.FormClosed += (_, _) => AppLog.Write("UI form closed");
    }

    private static void AttachRecursive(Control control)
    {
        switch (control)
        {
            case Button button: button.Click += (_, _) => AppLog.Write($"UI click button={Safe(button.Text)}"); break;
            case CheckBox checkBox: checkBox.CheckedChanged += (_, _) => AppLog.Write($"UI checkbox text={Safe(checkBox.Text)} checked={checkBox.Checked}"); break;
            case ComboBox combo: combo.SelectedIndexChanged += (_, _) => AppLog.Write($"UI combo selected={Safe(combo.SelectedItem?.ToString())}"); break;
            case TextBox textBox: textBox.Leave += (_, _) => AppLog.Write($"UI textbox leave value={Safe(textBox.Text)}"); break;
            case LinkLabel link: link.LinkClicked += (_, _) => AppLog.Write($"UI link click text={Safe(link.Text)}"); break;
        }
        control.ControlAdded += (_, e) => { if (e.Control is not null) AttachRecursive(e.Control); };
        foreach (Control child in control.Controls) AttachRecursive(child);
    }

    private static string Safe(string? value) => string.IsNullOrEmpty(value) ? "<empty>" : value.Replace("\r", " ").Replace("\n", " ");
}

internal static class UiInteractionFix
{
    public static void Attach(Form form)
    {
        var controls = Descendants(form).ToArray();
        foreach (var timeBox in controls.OfType<TimeTextBox>())
        {
            timeBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode is not (Keys.Enter or Keys.Return)) return;
                e.SuppressKeyPress = true; e.Handled = true;
                AppLog.Write($"UI time normalize requested by Enter value={timeBox.Text}");
                form.ActiveControl = null;
            };
        }
        AttachLanguageState(form, controls);
        AttachCancelCloseState(form, controls);
        AttachBackgroundCommit(form, form);
    }

    private static void AttachLanguageState(Form form, Control[] controls)
    {
        var subtitles = controls.OfType<CheckBox>().FirstOrDefault(c => c.Text == "Subtitles");
        var audio = controls.OfType<CheckBox>().FirstOrDefault(c => c.Text == "Audio");
        var language = controls.OfType<ComboBox>().FirstOrDefault(c => c.Width != 72);
        if (subtitles is null || audio is null || language is null) return;
        void Update()
        {
            var projectMode = GetPrivateField<object>(form, "_projectLaunch") is not null;
            language.Enabled = !projectMode && ((subtitles.Enabled && subtitles.Checked) || audio.Checked);
        }
        subtitles.CheckedChanged += (_, _) => Update();
        subtitles.EnabledChanged += (_, _) => Update();
        audio.CheckedChanged += (_, _) => Update();
        audio.EnabledChanged += (_, _) => Update();
        form.Activated += (_, _) => Update();
        Update();
    }

    private static void AttachCancelCloseState(Form form, Control[] controls)
    {
        var cancel = controls.OfType<Button>().FirstOrDefault(button => button.Text == "Cancel");
        var input = controls.OfType<TextBox>().FirstOrDefault(box => box is not TimeTextBox && box.Width == 390);
        var subtitles = controls.OfType<CheckBox>().FirstOrDefault(c => c.Text == "Subtitles");
        var video = controls.OfType<CheckBox>().FirstOrDefault(c => c.Text == "Video");
        var audio = controls.OfType<CheckBox>().FirstOrDefault(c => c.Text == "Audio");
        if (cancel is null || input is null || subtitles is null || video is null || audio is null) return;

        var completed = false;
        var resetInProgress = false;
        var cancelMouseDown = false;

        void ResetForm()
        {
            if (resetInProgress || GetPrivateField<bool>(form, "_busy")) return;
            resetInProgress = true;
            try
            {
                var projectMode = GetPrivateField<object>(form, "_projectLaunch") is not null;
                input.Clear();
                if (!projectMode)
                {
                    subtitles.Checked = subtitles.Enabled;
                    video.Checked = false;
                    audio.Checked = false;
                }
                InvokePrivate(form, "ClearState", false);
                completed = false;
                cancel.Text = "Cancel";
                AppLog.Write("UI", "form reset by Cancel");
                input.Focus();
            }
            finally { resetInProgress = false; }
        }

        input.TextChanged += (_, _) =>
        {
            if (resetInProgress || string.IsNullOrWhiteSpace(input.Text)) return;
            completed = false;
            cancel.Text = "Cancel";
        };

        cancel.MouseDown += (_, _) => cancelMouseDown = true;
        form.FormClosing += (_, e) =>
        {
            if (!cancelMouseDown) return;
            cancelMouseDown = false;
            if (completed || string.Equals(cancel.Text, "Close", StringComparison.Ordinal)) return;
            e.Cancel = true;
            ResetForm();
        };
        cancel.Click += (_, _) =>
        {
            cancelMouseDown = false;
            if (completed || string.Equals(cancel.Text, "Close", StringComparison.Ordinal)) { form.Close(); return; }
            ResetForm();
        };

        foreach (var action in controls.OfType<Button>().Where(button => button.Text is "Download" or "Replace" or "Move"))
        {
            action.Click += async (_, _) =>
            {
                if (!action.Enabled) return;
                var started = DateTime.UtcNow;
                var before = SnapshotOutputFiles(GetLastOutputDirectory(form));
                var sawBusy = false;
                for (var i = 0; i < 7200 && !form.IsDisposed; i++)
                {
                    await Task.Delay(100);
                    var busy = GetPrivateField<bool>(form, "_busy");
                    sawBusy |= busy;
                    if (!sawBusy || busy) continue;
                    await Task.Delay(250);
                    if (HasNewCompletedOutput(GetLastOutputDirectory(form), before, started))
                    {
                        completed = true;
                        cancel.Text = "Close";
                        AppLog.Write("UI", "successful normal download changed Cancel to Close");
                    }
                    break;
                }
            };
        }
    }

    private static string GetLastOutputDirectory(Form form)
    {
        var config = GetPrivateField<object>(form, "_config");
        return config?.GetType().GetProperty("LastOutputDirectory")?.GetValue(config) as string ?? string.Empty;
    }

    private static Dictionary<string, DateTime> SnapshotOutputFiles(string directory)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory)) return result;
        try { foreach (var file in Directory.EnumerateFiles(directory)) result[file] = File.GetLastWriteTimeUtc(file); } catch { }
        return result;
    }

    private static bool HasNewCompletedOutput(string directory, Dictionary<string, DateTime> before, DateTime started)
    {
        if (!Directory.Exists(directory)) return false;
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".srt", ".sub", ".txt", ".vtt", ".mp4", ".mp3" };
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!extensions.Contains(Path.GetExtension(file))) continue;
                var modified = File.GetLastWriteTimeUtc(file);
                if (modified < started.AddSeconds(-1)) continue;
                if (!before.TryGetValue(file, out var oldModified) || modified > oldModified) return true;
            }
        }
        catch { }
        return false;
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        try
        {
            var value = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance);
            return value is T typed ? typed : default!;
        }
        catch { return default!; }
    }

    private static void InvokePrivate(object instance, string name, params object[] args)
    {
        try { instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(instance, args); }
        catch (Exception ex) { AppLog.Exception($"UI invoke {name}", ex); }
    }

    private static void AttachBackgroundCommit(Control control, Form form)
    {
        if (control is not TextBox && control is not ComboBox && control is not Button && control is not CheckBox && control is not LinkLabel)
        {
            control.MouseDown += (_, _) =>
            {
                if (form.ActiveControl is TimeTextBox)
                {
                    AppLog.Write("UI time normalize requested by background click");
                    form.ActiveControl = null;
                }
            };
        }
        foreach (Control child in control.Controls) AttachBackgroundCommit(child, form);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

internal static class UiLayoutFix
{
    public static void Apply(Form form)
    {
        var stopwatch = Stopwatch.StartNew();
        var rootTable = form.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (rootTable is not null)
        {
            form.SuspendLayout(); form.Controls.Remove(rootTable); form.Padding = Padding.Empty;
            rootTable.Margin = Padding.Empty; rootTable.Padding = Padding.Empty;
            var shell = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(14) };
            shell.Controls.Add(rootTable); form.Controls.Add(shell); form.ResumeLayout(true);
        }
        var checkboxes = Descendants(form).OfType<CheckBox>().ToArray();
        if (checkboxes.Length >= 3)
        {
            var outputs = checkboxes[0].Parent as FlowLayoutPanel;
            var outputRow = outputs?.Parent as TableLayoutPanel;
            if (outputs is not null && outputRow is not null)
            {
                outputs.AutoSize = false; outputs.Height = 24; outputs.WrapContents = false; outputs.Margin = Padding.Empty; outputs.Padding = Padding.Empty; outputs.Dock = DockStyle.Fill;
                outputRow.AutoSize = false; outputRow.Height = 24; outputRow.MinimumSize = new Size(390, 24); outputRow.MaximumSize = new Size(390, 24); outputRow.Margin = Padding.Empty; outputRow.Padding = Padding.Empty; outputRow.RowCount = 1; outputRow.RowStyles.Clear(); outputRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                foreach (Control child in outputRow.Controls)
                {
                    child.Margin = Padding.Empty;
                    if (child is FlowLayoutPanel flow) { flow.WrapContents = false; flow.Height = 24; flow.Padding = Padding.Empty; }
                }
                if (outputRow.Parent is TableLayoutPanel table)
                {
                    var row = table.GetRow(outputRow);
                    if (row >= 0) { while (table.RowStyles.Count <= row) table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.RowStyles[row] = new RowStyle(SizeType.Absolute, 24); }
                    var times = table.Controls.Cast<Control>().OfType<FlowLayoutPanel>().FirstOrDefault(flow => flow.Controls.OfType<Label>().Any(label => label.Text == "From") && flow.Controls.OfType<Label>().Any(label => label.Text == "To"));
                    if (times is not null)
                    {
                        times.Margin = Padding.Empty; times.Padding = Padding.Empty;
                        var timesRow = table.GetRow(times);
                        if (timesRow >= 0) { while (table.RowStyles.Count <= timesRow) table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.RowStyles[timesRow] = new RowStyle(SizeType.AutoSize); }
                    }
                }
            }
        }
        AppLog.Write($"UI layout normalized elapsed={stopwatch.Elapsed.TotalMilliseconds:0.0}ms");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}