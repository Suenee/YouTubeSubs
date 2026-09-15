namespace YouTubeSubs;

internal static class ResetTimeFieldsFix
{
    public static void Attach(Form form)
    {
        var cancel = Descendants(form).OfType<Button>()
            .FirstOrDefault(button => button.Text is "Cancel" or "Close");
        var timeBoxes = Descendants(form).OfType<TimeTextBox>().ToArray();
        if (cancel is null || timeBoxes.Length == 0) return;

        cancel.Click += (_, _) =>
        {
            // UiInteractionFix handles Cancel first and changes the reset state to Close.
            // Clear the range only after that reset; Close itself must remain a pure close action.
            if (!string.Equals(cancel.Text, "Close", StringComparison.Ordinal)) return;
            foreach (var timeBox in timeBoxes) timeBox.Clear();
            AppLog.Write("UI", "time range cleared by form reset");
        };
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
