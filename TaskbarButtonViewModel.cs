using System.Windows.Media;
using System.Windows;

namespace DesktopTuner;

public sealed record TaskbarButtonViewModel(string Label, string ToolTip, ImageSource? Icon, object Target, int IconPixels, bool ShowLabel, Thickness ButtonMargin, Thickness IconMargin, bool IsRunning, bool IsActive, Brush AuraBrush, Brush AuraSolidBrush, bool AuraEnabled, bool DynamicAura)
{
    public string Initial
    {
        get
        {
            var trimmed = Label.Trim();
            return trimmed.Length == 0 ? "?" : trimmed[..1].ToUpperInvariant();
        }
    }

    public static TaskbarButtonViewModel FromPin(PinnedTaskbarApp app, DesktopPreferences preferences, bool vertical, bool isRunning = false, bool isActive = false, bool? showLabels = null) =>
        Create(app.Name, app.ExecutablePath, app, preferences, vertical, isRunning: isRunning, isActive: isActive, showLabels: showLabels);

    public static TaskbarButtonViewModel FromWindowGroup(TaskbarWindowGroup group, DesktopPreferences preferences, bool vertical, bool? showLabels = null) =>
        Create(group.Label, group.ToolTip, group, preferences, vertical, group.Windows[0].ExecutablePath, isRunning: true, isActive: group.IsActive, showLabels: showLabels);

    private static TaskbarButtonViewModel Create(string label, string toolTip, object target, DesktopPreferences preferences, bool vertical, string? iconPath = null, bool isRunning = false, bool isActive = false, bool? showLabels = null)
    {
        var resolvedIconPath = iconPath ?? (target as PinnedTaskbarApp)?.ExecutablePath ?? string.Empty;
        var isShellNamespace = target is PinnedTaskbarApp { IsShellNamespace: true };
        var icon = isShellNamespace ? TaskbarIconService.LoadNamespaceIcon(resolvedIconPath) : TaskbarIconService.LoadIcon(resolvedIconPath);
        var auraColor = isShellNamespace
            ? TaskbarAuraColorPolicy.ResolvePrimaryColor(icon) ?? TaskbarTheme.ReadAccentColor()
            : TaskbarIconService.GetPrimaryColor(resolvedIconPath) ?? TaskbarTheme.ReadAccentColor();
        var auraBrush = TaskbarAuraColorPolicy.CreateBrush(auraColor);
        var auraSolidBrush = new SolidColorBrush(auraColor);
        var auraEnabled = preferences.TaskbarButtonEffect != TaskbarButtonEffect.Accent;
        var effectiveShowLabels = showLabels ?? preferences.TaskbarShowLabels;
        return new(label, toolTip, icon, target,
            TaskbarIconSizePolicy.GetPixels(preferences.TaskbarIconSize), effectiveShowLabels,
            TaskbarButtonSpacingPolicy.GetButtonMargin(preferences.TaskbarButtonSpacing, vertical),
            effectiveShowLabels ? new Thickness(0, 0, 8, 0) : new Thickness(0), isRunning, isActive,
            auraBrush, auraSolidBrush, auraEnabled, preferences.TaskbarButtonEffect == TaskbarButtonEffect.DynamicAura);
    }
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
