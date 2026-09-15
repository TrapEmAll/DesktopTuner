namespace DesktopTuner;

public static class ShellHostLaunchPolicy
{
    public const string ShellHostWorkerArgument = "--shell-host-worker";
    public const int RequestedRestartExitCode = 1;
    public static TimeSpan PendingInvocationForwardTimeout => CustomShellPolicy.HostStartupReadinessTimeout + TimeSpan.FromSeconds(5);

    public static bool IsShellHostInvocation(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Any(argument => string.Equals(argument, "--shell-host", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, ShellHostWorkerArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsShellHostWorkerInvocation(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains(ShellHostWorkerArgument, StringComparer.OrdinalIgnoreCase);
    }

    public static bool ShouldRunShellHostSupervisor(IEnumerable<string> arguments, bool customShellPolicyTargetsApp)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = arguments.ToArray();
        if (values.Any(argument => string.Equals(argument, ShellHostWorkerArgument, StringComparison.OrdinalIgnoreCase))) return false;
        return customShellPolicyTargetsApp || values.Any(argument => string.Equals(argument, "--shell-host", StringComparison.OrdinalIgnoreCase));
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
        ShouldUseNativeTrayIntegration(false, shellOverlayMode, replaceNativeTaskbarPreference);

    public static bool ShouldUseNativeTrayIntegration(bool shellHostMode, bool shellOverlayMode, bool replaceNativeTaskbarPreference) =>
        !shellHostMode && (shellOverlayMode || !replaceNativeTaskbarPreference);

    public static bool ShouldUseNativeTrayIntegration(bool shellHostMode, bool shellOverlayMode, bool replaceNativeTaskbarPreference, bool nativeTrayAvailable) =>
        shellOverlayMode || (shellHostMode ? nativeTrayAvailable : !replaceNativeTaskbarPreference);

    public static bool ShouldReconcileNativeTrayIntegration(bool shellHostMode, bool currentIntegration, bool nativeTrayAvailable) =>
        shellHostMode && currentIntegration != nativeTrayAvailable;

    public static bool ShouldReserveShellHostWorkArea(bool shellHostMode, bool nativeTrayIntegrated) =>
        shellHostMode && !nativeTrayIntegrated;

    public static bool ShouldReplaceExplorerShortcut(bool shellHostMode, bool preference) =>
        shellHostMode || preference;

    public static bool ShouldRouteDesktopFoldersToCompanionExplorer(bool shellHostMode) => shellHostMode;

    public static bool ShouldRouteStartMenuLocationToCompanionExplorer(bool shellHostMode, bool isFilesystemDirectory, bool isShellNamespaceLocation) =>
        shellHostMode && (isFilesystemDirectory || isShellNamespaceLocation);

    public static bool ShouldLaunchExplorerOnShellHostExit(bool shellHostMode, bool customShellSupervisorActive) =>
        shellHostMode && !customShellSupervisorActive;

    public static bool ShouldRestoreExplorerAfterShellHostExit(bool shellLauncherHost, bool sessionEnding, int exitCode) =>
        shellLauncherHost && !sessionEnding && exitCode == 0;

    public static string GetExitLabel(bool shellHostMode) =>
        shellHostMode ? "Exit shell replacement and start Explorer" : "Exit Desktop Tuner";

    public static string GetRestartLabel(bool shellHostMode) =>
        shellHostMode ? "Restart shell replacement" : "Restart Desktop Tuner";

    public static bool ShouldAllowTaskbarClose(bool shellHostMode) => !shellHostMode;
}
