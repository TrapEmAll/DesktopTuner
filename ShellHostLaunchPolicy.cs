namespace DesktopTuner;

public static class ShellHostLaunchPolicy
{
    public static bool IsShellHostInvocation(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains("--shell-host", StringComparer.OrdinalIgnoreCase);
    }

    public static bool ShouldStartTaskbar(bool shellHostMode, bool startWithWindows) => shellHostMode || startWithWindows;

    public static bool ShouldCoverAllDisplays(bool shellHostMode, bool allDisplaysPreference) => shellHostMode || allDisplaysPreference;

    public static bool ShouldHideNativeTaskbar(bool shellHostMode, bool replaceNativeTaskbarPreference) =>
        !shellHostMode && replaceNativeTaskbarPreference;
}
