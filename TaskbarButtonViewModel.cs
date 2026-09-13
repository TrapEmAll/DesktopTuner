using System.Windows.Media;

namespace DesktopTuner;

public sealed record TaskbarButtonViewModel(string Label, string ToolTip, ImageSource? Icon, object Target, int IconPixels, bool ShowLabel)
{
    public string Initial
    {
        get
        {
            var trimmed = Label.Trim();
            return trimmed.Length == 0 ? "?" : trimmed[..1].ToUpperInvariant();
        }
    }

    public static TaskbarButtonViewModel FromPin(PinnedTaskbarApp app, DesktopPreferences preferences) =>
        new(app.Name, app.ExecutablePath, TaskbarIconService.LoadIcon(app.ExecutablePath), app, TaskbarIconSizePolicy.GetPixels(preferences.TaskbarIconSize), preferences.TaskbarShowLabels);

    public static TaskbarButtonViewModel FromWindowGroup(TaskbarWindowGroup group, DesktopPreferences preferences) =>
        new(group.Label, group.ToolTip, TaskbarIconService.LoadIcon(group.Windows[0].ExecutablePath), group, TaskbarIconSizePolicy.GetPixels(preferences.TaskbarIconSize), preferences.TaskbarShowLabels);
}

public static class TaskbarIconSizePolicy
{
    public static int GetPixels(TaskbarIconSize size) => size switch
    {
        TaskbarIconSize.Small => 16,
        TaskbarIconSize.Standard => 20,
        TaskbarIconSize.Large => 24,
        _ => throw new ArgumentOutOfRangeException(nameof(size))
    };
}
