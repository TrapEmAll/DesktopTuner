using System.Windows.Media;
using System.Windows;

namespace DesktopTuner;

public sealed record TaskbarButtonViewModel(string Label, string ToolTip, ImageSource? Icon, object Target, int IconPixels, bool ShowLabel, Thickness ButtonMargin, Thickness IconMargin, bool IsRunning = false, bool IsActive = false)
{
    public string Initial
    {
        get
        {
            var trimmed = Label.Trim();
            return trimmed.Length == 0 ? "?" : trimmed[..1].ToUpperInvariant();
        }
    }

    public static TaskbarButtonViewModel FromPin(PinnedTaskbarApp app, DesktopPreferences preferences, bool vertical, bool isRunning = false, bool isActive = false) =>
        Create(app.Name, app.ExecutablePath, app, preferences, vertical, isRunning: isRunning, isActive: isActive);

    public static TaskbarButtonViewModel FromWindowGroup(TaskbarWindowGroup group, DesktopPreferences preferences, bool vertical) =>
        Create(group.Label, group.ToolTip, group, preferences, vertical, group.Windows[0].ExecutablePath, isRunning: true, isActive: group.IsActive);

    private static TaskbarButtonViewModel Create(string label, string toolTip, object target, DesktopPreferences preferences, bool vertical, string? iconPath = null, bool isRunning = false, bool isActive = false) =>
        new(label, toolTip, TaskbarIconService.LoadIcon(iconPath ?? (target as PinnedTaskbarApp)?.ExecutablePath ?? string.Empty), target,
            TaskbarIconSizePolicy.GetPixels(preferences.TaskbarIconSize), preferences.TaskbarShowLabels,
            TaskbarButtonSpacingPolicy.GetButtonMargin(preferences.TaskbarButtonSpacing, vertical),
            preferences.TaskbarShowLabels ? new Thickness(0, 0, 8, 0) : new Thickness(0), isRunning, isActive);
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
