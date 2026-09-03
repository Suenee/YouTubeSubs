using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YouTubeSubs;

internal enum ProjectMediaMode
{
    AudioVideo,
    BrollVideo,
}

internal sealed record ProjectLaunchOptions(ProjectMediaMode Mode, int RequestedId, string Project)
{
    public string ModeLabel => Mode == ProjectMediaMode.AudioVideo ? "AV" : "BROLL";

    public static bool TryParse(string[] args, out ProjectLaunchOptions? options, out string? error)
    {
        options = null;
        error = null;
        int? avid = null;
        int? broll = null;
        string? project = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (TryReadValue(args, ref i, arg, "--avid", out var avidValue))
            {
                if (!TryParseId(avidValue, out var id)) { error = "--avid requires a positive numeric ID."; return false; }
                avid = id;
                continue;
            }
            if (TryReadValue(args, ref i, arg, "--brollid", out var brollValue))
            {
                if (!TryParseId(brollValue, out var id)) { error = "--brollid requires a positive numeric ID."; return false; }
                broll = id;
                continue;
            }
            if (TryReadValue(args, ref i, arg, "--project", out var projectValue))
            {
                project = projectValue.Trim();
                continue;
            }

            error = $"Unknown GUI option '{arg}'.";
            return false;
        }

        if (avid is null && broll is null && project is null) return true;
        if (avid is not null && broll is not null) { error = "Use either --avid or --brollid, not both."; return false; }
        if (avid is null && broll is null) { error = "--project requires --avid or --brollid."; return false; }
        if (string.IsNullOrWhiteSpace(project)) { error = "--avid/--brollid requires --project."; return false; }
        if (project.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { error = "--project contains characters that are not valid in a Windows folder name."; return false; }

        options = avid is not null
            ? new ProjectLaunchOptions(ProjectMediaMode.AudioVideo, avid.Value, project)
            : new ProjectLaunchOptions(ProjectMediaMode.BrollVideo, broll!.Value, project);
        return true;
    }

    public string ToIpcMessage()
    {
        var projectBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Project));
        return $"PROJECT\t{ModeLabel}\t{RequestedId.ToString(CultureInfo.InvariantCulture)}\t{projectBase64}";
    }

    public static bool TryFromIpcMessage(string message, out ProjectLaunchOptions? options)
    {
        options = null;
        var parts = message.Split('\t');
        if (parts.Length != 4 || parts[0] != "PROJECT" || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) return false;
        ProjectMediaMode mode;
        if (parts[1] == "AV") mode = ProjectMediaMode.AudioVideo;
        else if (parts[1] == "BROLL") mode = ProjectMediaMode.BrollVideo;
        else return false;
        try
        {
            var project = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
            if (string.IsNullOrWhiteSpace(project)) return false;
            options = new ProjectLaunchOptions(mode, id, project);
            return true;
        }
        catch { return false; }
    }

    private static bool TryReadValue(string[] args, ref int index, string arg, string name, out string value)
    {
        value = string.Empty;
        if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(name.Length + 1)..];
            return true;
        }
        if (!string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return false;
        if (index + 1 >= args.Length) return true;
        value = args[++index];
        return true;
    }

    private static bool TryParseId(string value, out int id) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}

internal static class ProjectStorage
{
    private static readonly Regex NumberedClip = new(@"^(?<id>\d+)(?:\s*-\s*.*)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ResolveBrollDirectory(string editingRoot, string project)
    {
        if (string.IsNullOrWhiteSpace(editingRoot)) throw new InvalidOperationException("Project editing root is not configured.");
        Directory.CreateDirectory(editingRoot);
        var matches = Directory.EnumerateDirectories(editingRoot)
            .Where(path => IsProjectDirectory(Path.GetFileName(path), project))
            .ToList();
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"More than one dated directory exists for project '{project}'. Resolve the duplicate project directories before downloading.");
        }

        var projectDirectory = matches.Count == 1
            ? matches[0]
            : Path.Combine(editingRoot, $"{DateTime.Now:yyyyMMdd} {project}");
        Directory.CreateDirectory(projectDirectory);
        var broll = Path.Combine(projectDirectory, "BROLL");
        Directory.CreateDirectory(broll);
        return broll;
    }

    public static IReadOnlyList<string> FindClipsById(string brollDirectory, int id)
    {
        if (!Directory.Exists(brollDirectory)) return Array.Empty<string>();
        return Directory.EnumerateFiles(brollDirectory, "*.mp4", SearchOption.TopDirectoryOnly)
            .Where(path => TryGetClipId(path, out var found) && found == id)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static int FindNextFreeId(string brollDirectory, int requestedId)
    {
        var id = requestedId + 1;
        while (FindClipsById(brollDirectory, id).Count > 0)
        {
            if (id == int.MaxValue) throw new InvalidOperationException("No free project media ID is available.");
            id++;
        }
        return id;
    }

    public static string BuildClipPath(string brollDirectory, int id, string clipName)
        => Path.Combine(brollDirectory, $"{id:D3} - {clipName}.mp4");

    public static string SuggestClipName(string title, int maxWords)
    {
        var cleaned = YoutubeService.CleanFilename(title).Trim();
        var words = Regex.Split(cleaned, @"\s+").Where(word => word.Length > 0).Take(Math.Max(1, maxWords)).ToArray();
        return SanitizeClipName(words.Length == 0 ? "clip" : string.Join(' ', words), maxWords);
    }

    public static string SanitizeClipName(string value, int maxWords)
    {
        var cleaned = YoutubeService.CleanFilename(value).Trim();
        var words = Regex.Split(cleaned, @"\s+").Where(word => word.Length > 0).Take(Math.Max(1, maxWords)).ToArray();
        var result = string.Join(' ', words).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "clip" : result;
    }

    private static bool IsProjectDirectory(string name, string project)
    {
        if (name.Length <= 9 || name[8] != ' ' || !string.Equals(name[9..], project, StringComparison.OrdinalIgnoreCase)) return false;
        return DateTime.TryParseExact(name[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static bool TryGetClipId(string path, out int id)
    {
        id = 0;
        var match = NumberedClip.Match(Path.GetFileNameWithoutExtension(path));
        return match.Success && int.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }
}

internal enum ClipCollisionChoice
{
    Cancel,
    Replace,
    Move,
}

internal sealed class ClipCollisionDialog : Form
{
    public ClipCollisionChoice Choice { get; private set; } = ClipCollisionChoice.Cancel;

    public ClipCollisionDialog(Form owner, int currentId, int freeId)
    {
        Text = "YouTubeSubs";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);
        Icon = owner.Icon;

        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(new Label { AutoSize = true, Text = $"Media ID {currentId} already exists in this project's BROLL folder." });
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 12, 0, 0) };
        var replace = new Button { AutoSize = true, Text = "Replace" };
        var move = new Button { AutoSize = true, Text = $"Move to {freeId}" };
        var cancel = new Button { AutoSize = true, Text = "Cancel" };
        replace.Click += (_, _) => Finish(ClipCollisionChoice.Replace);
        move.Click += (_, _) => Finish(ClipCollisionChoice.Move);
        cancel.Click += (_, _) => Finish(ClipCollisionChoice.Cancel);
        buttons.Controls.Add(replace);
        buttons.Controls.Add(move);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        CancelButton = cancel;
    }

    private void Finish(ClipCollisionChoice choice)
    {
        Choice = choice;
        DialogResult = choice == ClipCollisionChoice.Cancel ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }
}

internal static class MarkerClipboard
{
    public static void SetHtmlTemplate(string template, int id)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{id}", StringComparison.Ordinal))
            throw new InvalidOperationException("The selected marker HTML template must contain the {id} placeholder.");
        var html = template.Replace("{id}", id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, html);
        data.SetData(DataFormats.Text, html);
        data.SetData(DataFormats.Html, BuildCfHtml(html));
        Clipboard.SetDataObject(data, true);
    }

    private static string BuildCfHtml(string fragment)
    {
        const string prefix = "<html><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";
        const string headerTemplate = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var zeroHeader = string.Format(CultureInfo.InvariantCulture, headerTemplate, 0, 0, 0, 0);
        var startHtml = Encoding.UTF8.GetByteCount(zeroHeader);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
        var header = string.Format(CultureInfo.InvariantCulture, headerTemplate, startHtml, endHtml, startFragment, endFragment);
        return header + prefix + fragment + suffix;
    }
}
