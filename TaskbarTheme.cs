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

    public static TaskbarPalette Resolve(bool dark, TaskbarVisualStyle style = TaskbarVisualStyle.Windows11)
    {
        if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style));
        string Read(string key) => dark ? Colors[key].Dark : Colors[key].Light;
        var palette = new TaskbarPalette(
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
        palette = style switch
        {
            TaskbarVisualStyle.Windows11 => palette,
            TaskbarVisualStyle.Windows10 => palette with
            {
                Background = dark ? "#202020" : "#F0F0F0",
                Foreground = dark ? "#FFFFFF" : "#202020",
                MutedText = dark ? "#C6C6C6" : "#555555",
                Border = dark ? "#3A3A3A" : "#C8C8C8",
                Hover = dark ? "#3A3A3A" : "#DDDDDD",
                Pressed = dark ? "#505050" : "#CCCCCC",
                RunningIndicator = "#0078D7",
                SegmentBorder = dark ? "#444444" : "#BDBDBD",
                ClockSurface = dark ? "#292929" : "#E5E5E5",
                DateText = dark ? "#C6C6C6" : "#555555",
                EmptyText = dark ? "#C6C6C6" : "#555555",
                PreviewCard = dark ? "#252525" : "#FFFFFF",
                PreviewBorder = dark ? "#505050" : "#C8C8C8",
                PreviewSurface = dark ? "#202020" : "#F0F0F0"
            },
            TaskbarVisualStyle.Windows7 => palette with
            {
                Background = "#20384C",
                Foreground = "#FFFFFF",
                MutedText = "#D6E2EC",
                Border = "#71869A",
                Hover = "#526F88",
                Pressed = "#395A76",
                RunningIndicator = "#72C7FF",
                SegmentBorder = "#71869A",
                ClockSurface = "#304B62",
                DateText = "#E0EAF2",
                EmptyText = "#D6E2EC",
                PreviewCard = "#20384C",
                PreviewBorder = "#71869A",
                PreviewSurface = "#20384C"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
        if (!SystemAccentColorService.TryRead(out var colorizationColor)) return palette;
        var (accent, fallback) = ResolveAccentBrushes(colorizationColor);
        return palette with { Accent = accent, AccentFallback = fallback };
    }

    public static (string Accent, string Fallback) ResolveAccentBrushes(uint colorizationColor)
        => SystemAccentColorService.ResolveTaskbarAccent(colorizationColor);

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

    public static void Apply(bool dark, TaskbarVisualStyle style = TaskbarVisualStyle.Windows11)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        var palette = Resolve(dark, style);
        foreach (var (key, colors) in Colors)
        {
            var colorText = key switch
            {
                "TaskbarBackgroundBrush" => palette.Background,
                "TaskbarForegroundBrush" => palette.Foreground,
                "TaskbarMutedTextBrush" => palette.MutedText,
                "TaskbarBorderBrush" => palette.Border,
                "TaskbarHoverBrush" => palette.Hover,
                "TaskbarPressedBrush" => palette.Pressed,
                "TaskbarRunningIndicatorBrush" => palette.RunningIndicator,
                "TaskbarSegmentBorderBrush" => palette.SegmentBorder,
                "TaskbarAccentBrush" => palette.Accent,
                "TaskbarAccentFallbackBrush" => palette.AccentFallback,
                "TaskbarClockSurfaceBrush" => palette.ClockSurface,
                "TaskbarDateTextBrush" => palette.DateText,
                "TaskbarEmptyTextBrush" => palette.EmptyText,
                "TaskbarPreviewCardBrush" => palette.PreviewCard,
                "TaskbarPreviewBorderBrush" => palette.PreviewBorder,
                "TaskbarPreviewSurfaceBrush" => palette.PreviewSurface,
                _ => dark ? colors.Dark : colors.Light
            };
            var color = (Color)ColorConverter.ConvertFromString(colorText);
            resources[key] = new SolidColorBrush(color);
        }
        if (SystemAccentColorService.TryRead(out var colorizationColor))
        {
            var (accent, fallback) = ResolveAccentBrushes(colorizationColor);
            resources["TaskbarAccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
            resources["TaskbarAccentFallbackBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
        }
        resources["TaskbarButtonCornerRadius"] = new CornerRadius(style == TaskbarVisualStyle.Windows11 ? 8 : 1);
        resources["TaskbarSegmentCornerRadius"] = new CornerRadius(style == TaskbarVisualStyle.Windows11 ? 11 : 0);
        resources["TaskbarButtonBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            style == TaskbarVisualStyle.Windows11 ? "#00000000" : palette.Border));
        resources["TaskbarButtonBorderThickness"] = new Thickness(style == TaskbarVisualStyle.Windows11 ? 0 : 1);
        SystemBackdropService.RefreshOpenWindowDarkMode();
    }

    public static Color ReadAccentColor() => GetBrush("TaskbarAccentBrush") is SolidColorBrush brush
        ? brush.Color
        : Color.FromRgb(98, 88, 217);

    public static Brush GetBrush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;

    public static Brush CreateBackground(bool dark, int transparencyPercent, TaskbarVisualStyle style = TaskbarVisualStyle.Windows11)
    {
        var alpha = TaskbarTransparencyPolicy.GetAlpha(transparencyPercent);
        if (style == TaskbarVisualStyle.Windows7)
        {
            Color WithAlpha(string value)
            {
                var color = (Color)ColorConverter.ConvertFromString(value);
                return Color.FromArgb(alpha, color.R, color.G, color.B);
            }
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            gradient.GradientStops.Add(new GradientStop(WithAlpha("#526A7D"), 0));
            gradient.GradientStops.Add(new GradientStop(WithAlpha("#263E53"), 0.18));
            gradient.GradientStops.Add(new GradientStop(WithAlpha("#162A3B"), 1));
            return gradient;
        }
        var colorText = Resolve(dark, style).Background;
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public static bool UsesBackdrop(TaskbarVisualStyle style) => style == TaskbarVisualStyle.Windows11;
}
