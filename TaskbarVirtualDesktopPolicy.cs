namespace DesktopTuner;

public static class TaskbarVirtualDesktopPolicy
{
    public static IReadOnlyList<RunningWindow> Filter(IEnumerable<RunningWindow> windows, bool showAllVirtualDesktops)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var entries = windows.ToList();
        return showAllVirtualDesktops
            ? entries
            : entries.Where(window => window.IsOnCurrentVirtualDesktop is not false).ToList();
    }
}
