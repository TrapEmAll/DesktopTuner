namespace DesktopTuner;

public static class ShellHostLaunchPolicy
{
    public static bool IsShellHostInvocation(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains("--shell-host", StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsShellOverlayInvocation(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains("--shell-overlay", StringComparer.OrdinalIgnoreCase);
    }

    public static bool ShouldStartTaskbar(bool shellHostMode, bool startWithWindows) => shellHostMode || startWithWindows;

    public static bool ShouldStartTaskbar(bool shellHostMode, bool shellOverlayMode, bool startWithWindows) =>
        shellHostMode || shellOverlayMode || startWithWindows;

    public static bool ShouldCoverAllDisplays(bool shellHostMode, bool allDisplaysPreference) => shellHostMode || allDisplaysPreference;

    public static bool ShouldCoverAllDisplays(bool shellHostMode, bool allDisplaysPreference, bool shellOverlayMode) =>
        shellHostMode || shellOverlayMode || allDisplaysPreference;

    public static bool ShouldHideNativeTaskbar(bool shellHostMode, bool replaceNativeTaskbarPreference) =>
        !shellHostMode && replaceNativeTaskbarPreference;

    public static bool ShouldHideNativeTaskbar(bool shellHostMode, bool shellOverlayMode, bool replaceNativeTaskbarPreference) =>
        !shellHostMode && !shellOverlayMode && replaceNativeTaskbarPreference;

    public static bool ShouldUseNativeTrayIntegration(bool shellOverlayMode, bool replaceNativeTaskbarPreference) =>
        shellOverlayMode || !replaceNativeTaskbarPreference;

    public static bool ShouldLaunchExplorerOnShellHostExit(bool shellHostMode, bool customShellSupervisorActive) =>
        shellHostMode && !customShellSupervisorActive;

    public static bool ShouldRestoreExplorerAfterShellHostExit(bool shellLauncherHost, bool sessionEnding, int exitCode) =>
        shellLauncherHost && !sessionEnding && exitCode == 0;
}
