using System.Windows;
using System.Windows.Media;

namespace DesktopTuner;

public static class DesktopTheme
{
    private static readonly IReadOnlyDictionary<string, (string Light, string Dark)> Palette = new Dictionary<string, (string, string)>
    {
        ["DesktopWindowBrush"] = ("#F5F6FA", "#171A21"),
        ["DesktopSidebarBrush"] = ("#F5F6FA", "#171A21"),
        ["DesktopSidebarSurfaceBrush"] = ("#EBEEF5", "#20242D"),
        ["DesktopSidebarTextBrush"] = ("#172033", "#F3F4F6"),
        ["DesktopSidebarMutedTextBrush"] = ("#697386", "#B7BECA"),
        ["DesktopSidebarLabelBrush"] = ("#8992A3", "#8F98A8"),
        ["DesktopSidebarHoverBrush"] = ("#E7E9EF", "#2D3442"),
        ["DesktopSurfaceBrush"] = ("#FFFFFF", "#20242D"),
        ["DesktopSurfaceAltBrush"] = ("#F9FAFD", "#252A35"),
        ["DesktopPrimaryTextBrush"] = ("#172033", "#F3F4F6"),
        ["DesktopMutedTextBrush"] = ("#697386", "#B7BECA"),
        ["DesktopSoftTextBrush"] = ("#7B8495", "#A5ADBA"),
        ["DesktopFaintTextBrush"] = ("#8992A3", "#8F98A8"),
        ["DesktopBorderBrush"] = ("#DDE1EA", "#363D49"),
        ["DesktopInputBorderBrush"] = ("#D7DCE6", "#454D5B"),
        ["DesktopHoverBrush"] = ("#EFF0FB", "#2D3442"),
        ["DesktopSelectedBrush"] = ("#E8E7FB", "#393455"),
        ["DesktopAccentTintBrush"] = ("#ECEBFA", "#312E4D"),
        ["DesktopAccentTextBrush"] = ("#5148C7", "#B9B4FF"),
        ["DesktopAccentButtonBrush"] = ("#6258D9", "#6258D9"),
        ["DesktopDividerBrush"] = ("#E7E9EF", "#353C48")
    };

    public static void Apply(bool dark)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        foreach (var (key, colors) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(dark ? colors.Dark : colors.Light);
            resources[key] = new SolidColorBrush(color);
        }

        if (SystemAccentColorService.TryRead(out var colorizationColor))
        {
            var (tint, text) = SystemAccentColorService.ResolveDesktopAccent(dark, colorizationColor);
            resources["DesktopAccentTintBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tint));
            resources["DesktopAccentTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
        }

        SystemBackdropService.RefreshOpenWindowDarkMode();
    }
}
