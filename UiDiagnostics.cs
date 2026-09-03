using System.Diagnostics;

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
            case Button button:
                button.Click += (_, _) => AppLog.Write($"UI click button={Safe(button.Text)}");
                break;
            case CheckBox checkBox:
                checkBox.CheckedChanged += (_, _) => AppLog.Write($"UI checkbox text={Safe(checkBox.Text)} checked={checkBox.Checked}");
                break;
            case ComboBox combo:
                combo.SelectedIndexChanged += (_, _) => AppLog.Write($"UI combo selected={Safe(combo.SelectedItem?.ToString())}");
                break;
            case TextBox textBox:
                textBox.Leave += (_, _) => AppLog.Write($"UI textbox leave value={Safe(textBox.Text)}");
                break;
            case LinkLabel link:
                link.LinkClicked += (_, _) => AppLog.Write($"UI link click text={Safe(link.Text)}");
                break;
        }

        control.ControlAdded += (_, e) => AttachRecursive(e.Control);
        foreach (Control child in control.Controls) AttachRecursive(child);
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "<empty>";
        return value.Replace("\r", " ").Replace("\n", " ");
    }
}

internal static class UiLayoutFix
{
    public static void Apply(Form form)
    {
        var stopwatch = Stopwatch.StartNew();
        var checkboxes = Descendants(form).OfType<CheckBox>().ToArray();
        if (checkboxes.Length >= 3)
        {
            var outputs = checkboxes[0].Parent as FlowLayoutPanel;
            var outputRow = outputs?.Parent as TableLayoutPanel;
            if (outputs is not null && outputRow is not null)
            {
                outputs.AutoSize = false;
                outputs.Height = 24;
                outputs.WrapContents = false;
                outputs.Margin = Padding.Empty;
                outputs.Padding = Padding.Empty;
                outputs.Dock = DockStyle.Fill;

                outputRow.AutoSize = false;
                outputRow.Height = 24;
                outputRow.MinimumSize = new Size(390, 24);
                outputRow.MaximumSize = new Size(390, 24);
                outputRow.Margin = Padding.Empty;
                outputRow.Padding = Padding.Empty;
                outputRow.RowCount = 1;
                outputRow.RowStyles.Clear();
                outputRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

                foreach (Control child in outputRow.Controls)
                {
                    child.Margin = Padding.Empty;
                    if (child is FlowLayoutPanel flow)
                    {
                        flow.WrapContents = false;
                        flow.Height = 24;
                        flow.Padding = Padding.Empty;
                    }
                }

                if (outputRow.Parent is TableLayoutPanel table)
                {
                    var row = table.GetRow(outputRow);
                    if (row >= 0)
                    {
                        while (table.RowStyles.Count <= row) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        table.RowStyles[row] = new RowStyle(SizeType.Absolute, 24);
                    }

                    var times = table.Controls.Cast<Control>()
                        .OfType<FlowLayoutPanel>()
                        .FirstOrDefault(flow => flow.Controls.OfType<Label>().Any(label => label.Text == "From") && flow.Controls.OfType<Label>().Any(label => label.Text == "To"));
                    if (times is not null)
                    {
                        times.Margin = Padding.Empty;
                        times.Padding = Padding.Empty;
                        var timesRow = table.GetRow(times);
                        if (timesRow >= 0)
                        {
                            while (table.RowStyles.Count <= timesRow) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                            table.RowStyles[timesRow] = new RowStyle(SizeType.AutoSize);
                        }
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
