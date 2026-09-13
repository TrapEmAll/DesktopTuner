using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DesktopTuner;

public sealed record TaskbarPalette(
    string Background,
    string Foreground,
    string MutedText,
    string Border,
    string Hover,
    string Pressed,
    string RunningIndicator,
    string SegmentBorder,
    string Accent,
    string AccentFallback,
    string ClockSurface,
    string DateText,
    string EmptyText,
    string PreviewCard,
    string PreviewBorder,
    string PreviewSurface);

public static class TaskbarTheme
{
    private static readonly IReadOnlyDictionary<string, (string Light, string Dark)> Colors = new Dictionary<string, (string, string)>
    {
        ["TaskbarBackgroundBrush"] = ("#F3F5FA", "#171D2A"),
        ["TaskbarForegroundBrush"] = ("#1B2434", "#F4F6FA"),
        ["TaskbarMutedTextBrush"] = ("#586478", "#B9C2D5"),
        ["TaskbarBorderBrush"] = ("#CED4E0", "#405064"),
        ["TaskbarHoverBrush"] = ("#E5E9F2", "#30394D"),
        ["TaskbarPressedBrush"] = ("#D8DDED", "#494F70"),
        ["TaskbarRunningIndicatorBrush"] = ("#7A8497", "#9AA5B8"),
        ["TaskbarSegmentBorderBrush"] = ("#D6DBE5", "#46516A"),
        ["TaskbarAccentBrush"] = ("#6258D9", "#827AF0"),
        ["TaskbarAccentFallbackBrush"] = ("#6258D9", "#8D86FF"),
        ["TaskbarClockSurfaceBrush"] = ("#E8ECF4", "#30394D"),
        ["TaskbarDateTextBrush"] = ("#697386", "#B9C2D5"),
        ["TaskbarEmptyTextBrush"] = ("#697386", "#8993A7"),
        ["TaskbarPreviewCardBrush"] = ("#FFFFFF", "#242A37"),
        ["TaskbarPreviewBorderBrush"] = ("#D6DBE5", "#525E74"),
        ["TaskbarPreviewSurfaceBrush"] = ("#F3F5FA", "#171D2A")
    };

    public static TaskbarPalette Resolve(bool dark)
    {
        string Read(string key) => dark ? Colors[key].Dark : Colors[key].Light;
        return new TaskbarPalette(
            Read("TaskbarBackgroundBrush"),
            Read("TaskbarForegroundBrush"),
            Read("TaskbarMutedTextBrush"),
            Read("TaskbarBorderBrush"),
            Read("TaskbarHoverBrush"),
            Read("TaskbarPressedBrush"),
            Read("TaskbarRunningIndicatorBrush"),
            Read("TaskbarSegmentBorderBrush"),
            Read("TaskbarAccentBrush"),
            Read("TaskbarAccentFallbackBrush"),
            Read("TaskbarClockSurfaceBrush"),
            Read("TaskbarDateTextBrush"),
            Read("TaskbarEmptyTextBrush"),
            Read("TaskbarPreviewCardBrush"),
            Read("TaskbarPreviewBorderBrush"),
            Read("TaskbarPreviewSurfaceBrush"));
    }

    public static bool ReadSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsCatalog.Personalize);
            return key?.GetValue("SystemUsesLightTheme", 0) is not int lightMode || lightMode == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    public static void Apply(bool dark)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        foreach (var (key, colors) in Colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(dark ? colors.Dark : colors.Light);
            resources[key] = new SolidColorBrush(color);
        }
    }

    public static Brush GetBrush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;

    public static Brush CreateBackground(bool dark, int transparencyPercent)
    {
        var colorText = Resolve(dark).Background;
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        return new SolidColorBrush(Color.FromArgb(TaskbarTransparencyPolicy.GetAlpha(transparencyPercent), color.R, color.G, color.B));
    }
}
