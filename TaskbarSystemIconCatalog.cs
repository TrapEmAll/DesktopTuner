namespace DesktopTuner;

public static class TaskbarSystemIconCatalog
{
    public const string FluentFontFamily = "Segoe Fluent Icons";
    public const string LegacyFontFamily = "Segoe MDL2 Assets";

    public static string GetFontFamily(TaskbarVisualStyle style) => style switch
    {
        TaskbarVisualStyle.Windows11 => FluentFontFamily,
        TaskbarVisualStyle.Windows10 or TaskbarVisualStyle.Windows7 => LegacyFontFamily,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown taskbar visual style.")
    };

    public static string GetVolumeGlyph(float volume, bool muted)
    {
        if (muted || !float.IsFinite(volume)) return "\uE74F";
        if (volume <= 0) return "\uE992";
        if (volume < 0.34f) return "\uE993";
        if (volume < 0.67f) return "\uE994";
        return "\uE995";
    }

}
