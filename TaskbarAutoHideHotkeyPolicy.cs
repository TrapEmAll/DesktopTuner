namespace DesktopTuner;

public static class TaskbarAutoHideHotkeyPolicy
{
    public static DesktopPreferences Toggle(DesktopPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences with { AutoHide = !preferences.AutoHide };
    }
}
