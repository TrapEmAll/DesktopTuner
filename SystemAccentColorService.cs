using System.Runtime.InteropServices;
using System.Windows.Media;

namespace DesktopTuner;

public static class SystemAccentColorService
{
    public static bool TryRead(out uint colorizationColor)
    {
        colorizationColor = 0;
        try
        {
            return DwmGetColorizationColor(out colorizationColor, out _) >= 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static (string Accent, string Fallback) ResolveTaskbarAccent(uint colorizationColor)
    {
        var accent = ReadColor(colorizationColor);
        var fallback = Color.FromRgb(
            (byte)Math.Round(accent.R * 0.75),
            (byte)Math.Round(accent.G * 0.75),
            (byte)Math.Round(accent.B * 0.75));
        return (accent.ToString(), fallback.ToString());
    }

    public static (string Tint, string Text) ResolveDesktopAccent(bool dark, uint colorizationColor)
    {
        var accent = ReadColor(colorizationColor);
        var surface = ReadColor(ParseColor(dark ? "#252A35" : "#F9FAFD"));
        var tint = Blend(surface, accent, 0.12);
        var text = accent;
        var target = dark ? Colors.White : Colors.Black;
        for (var attempt = 0; attempt < 24 && ContrastRatio(text, tint) < 4.5; attempt++)
            text = Blend(text, target, 0.1);
        return (tint.ToString(), text.ToString());
    }

    private static Color ReadColor(uint argb) => Color.FromArgb(255, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static uint ParseColor(string value)
    {
        var color = (Color)ColorConverter.ConvertFromString(value);
        return 0xFF000000u | (uint)(color.R << 16) | (uint)(color.G << 8) | color.B;
    }

    private static Color Blend(Color color, Color target, double amount) => Color.FromRgb(
        BlendChannel(color.R, target.R, amount),
        BlendChannel(color.G, target.G, amount),
        BlendChannel(color.B, target.B, amount));

    private static byte BlendChannel(byte value, byte target, double amount) => (byte)Math.Round(value + (target - value) * amount);

    private static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(Color color) =>
        0.2126 * Linearize(color.R / 255d) +
        0.7152 * Linearize(color.G / 255d) +
        0.0722 * Linearize(color.B / 255d);

    private static double Linearize(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);
}
