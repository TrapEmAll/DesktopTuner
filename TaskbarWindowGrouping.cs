namespace DesktopTuner;

public sealed record TaskbarWindowGroup(string Label, string ApplicationName, IReadOnlyList<RunningWindow> Windows)
{
    public string ToolTip => string.Join(Environment.NewLine, Windows.Select(window => window.Title));
    public bool IsActive => Windows.Any(window => window.IsForeground);
}

public static class TaskbarWindowGrouping
{
    public static IReadOnlyList<TaskbarWindowGroup> Create(IEnumerable<RunningWindow> windows, TaskbarGroupingMode mode, int buttonCapacity, IEnumerable<PinnedTaskbarApp>? pinnedApps = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));

        var entries = windows.ToList();
        var remaining = entries.Where(window => pinnedApps is null || !pinnedApps.Any(app => MatchesPinnedApp(app, window))).ToList();
        var capacity = Math.Max(1, buttonCapacity);
        var shouldGroup = mode == TaskbarGroupingMode.Always
            || mode == TaskbarGroupingMode.WhenFull && remaining.Count > capacity;
        if (!shouldGroup)
            return remaining.Select(window => CreateGroup([window])).ToList();

        return remaining
            .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateGroup(group.ToList()))
            .ToList();
    }

    public static bool MatchesPinnedApp(PinnedTaskbarApp app, RunningWindow window) => TaskbarPinIdentityService.Matches(app, window);

    private static TaskbarWindowGroup CreateGroup(IReadOnlyList<RunningWindow> windows)
    {
        var first = windows[0];
        var label = windows.Count > 1 ? $"{first.ApplicationName} ({windows.Count})" : first.Title;
        return new TaskbarWindowGroup(label, first.ApplicationName, windows);
    }

    private static string GroupKey(RunningWindow window)
    {
        if (!string.IsNullOrWhiteSpace(window.ExecutablePath)) return $"path:{window.ExecutablePath}";
        if (!string.IsNullOrWhiteSpace(window.ApplicationName) && !string.Equals(window.ApplicationName, "Application", StringComparison.OrdinalIgnoreCase))
            return $"name:{window.ApplicationName}";
        return $"window:{window.Handle}";
    }
}
