namespace DesktopTuner;

public static class StartMenuAppNavigationPolicy
{
    public static bool ShouldShowProgramFolders(StartMenuStyle style, string query) =>
        (style is StartMenuStyle.Classic or StartMenuStyle.Windows7) && string.IsNullOrWhiteSpace(query);
}
