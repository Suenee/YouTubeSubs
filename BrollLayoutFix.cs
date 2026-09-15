namespace YouTubeSubs;

internal static class BrollLayoutFix
{
    public static void Apply(Form form)
    {
        var table = form.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (table is null) return;

        form.SuspendLayout();
        table.SuspendLayout();
        try
        {
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.Margin = Padding.Empty;
            table.Padding = Padding.Empty;
            table.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            table.RowStyles.Clear();
            for (var row = 0; row < table.RowCount; row++)
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            foreach (Control control in table.Controls)
            {
                if (control is Label label && label.Text is "Mode" or "Target" or "Next ID" or "Clip name")
                    label.Margin = new Padding(0, 4, 8, 0);
                else if (control is Label)
                    control.Margin = new Padding(0, 2, 0, 0);
                else if (control is TextBox)
                    control.Margin = new Padding(0, 2, 0, 2);
                else if (control is LinkLabel status)
                {
                    status.Height = 32;
                    status.Margin = Padding.Empty;
                }
                else if (control is FlowLayoutPanel flow)
                {
                    flow.Margin = Padding.Empty;
                    flow.Padding = Padding.Empty;
                }
            }

            var timeRow = table.Controls.OfType<FlowLayoutPanel>()
                .FirstOrDefault(flow => flow.Controls.OfType<Label>().Any(label => label.Text == "From"));
            if (timeRow is not null)
            {
                timeRow.Margin = new Padding(0, 2, 0, 2);
                timeRow.Padding = Padding.Empty;
            }

            var buttonRow = table.Controls.OfType<FlowLayoutPanel>()
                .FirstOrDefault(flow => flow.Controls.OfType<Button>().Any(button => button.Text == "Download"));
            if (buttonRow is not null)
            {
                buttonRow.Margin = Padding.Empty;
                buttonRow.Padding = Padding.Empty;
            }
        }
        finally
        {
            table.ResumeLayout(true);
            form.ResumeLayout(true);
        }

        AppLog.Write("UI", "global BROLL layout normalized");
    }
}
