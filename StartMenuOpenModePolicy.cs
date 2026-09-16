namespace DesktopTuner;

public static class StartMenuOpenModePolicy
{
    public static bool ShouldShowOverview(string? query, bool openAllApps) =>
        !openAllApps && string.IsNullOrWhiteSpace(query);
}
