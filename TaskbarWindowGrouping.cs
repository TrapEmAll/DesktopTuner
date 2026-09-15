using System.IO;

namespace DesktopTuner;

public sealed record TaskbarWindowGroup(string Label, string ApplicationName, IReadOnlyList<RunningWindow> Windows)
{
    public bool CanRunElevated => Windows.FirstOrDefault()?.CanRunElevated == true;
    public bool CanLaunchNewInstance => TaskbarWindowGrouping.GetLaunchPath(this) is not null;
    public bool CanOpenLocation => CanLaunchNewInstance;
    public string ToolTip => string.Join(Environment.NewLine, Windows.Select(window => window.Title));
    public bool IsActive => Windows.Any(window => window.IsForeground);
}

public static class TaskbarWindowGrouping
{
    public static string? GetLaunchPath(TaskbarWindowGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Windows
            .Select(window => window.ExecutablePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    public static RunningWindow? SelectCloseTarget(TaskbarWindowGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Windows.FirstOrDefault(window => window.IsForeground) ?? group.Windows.FirstOrDefault();
    }

    public static RunningWindow? SelectPinnedRepresentative(PinnedTaskbarApp app, IEnumerable<RunningWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(windows);
        var matches = windows.Where(window => MatchesPinnedApp(app, window)).ToList();
        return matches.FirstOrDefault(window => window.IsForeground) ?? matches.FirstOrDefault();
    }

    public static RunningWindow? SelectLastActivePinnedWindow(PinnedTaskbarApp app, IEnumerable<RunningWindow> windows, IEnumerable<nint> mostRecentFirstHandles)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(mostRecentFirstHandles);
        var matches = windows.Where(window => MatchesPinnedApp(app, window)).ToDictionary(window => window.Handle);
        foreach (var handle in mostRecentFirstHandles)
            if (matches.TryGetValue(handle, out var window)) return window;
        return null;
    }

    public static IReadOnlyList<TaskbarWindowGroup> Create(IEnumerable<RunningWindow> windows, TaskbarGroupingMode mode, int buttonCapacity, IEnumerable<PinnedTaskbarApp>? pinnedApps = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));

        var entries = windows.ToList();
        var pins = (pinnedApps ?? []).Where(app => !app.IsDirectory).ToList();
        var attachedWindowHandles = new HashSet<nint>();
        foreach (var app in pins)
        {
            if (mode == TaskbarGroupingMode.Never)
            {
                var representative = SelectPinnedRepresentative(app, entries);
                if (representative is not null) attachedWindowHandles.Add(representative.Handle);
            }
            else
            {
                foreach (var window in entries.Where(window => MatchesPinnedApp(app, window)))
                    attachedWindowHandles.Add(window.Handle);
            }
        }
        var remaining = entries.Where(window => !attachedWindowHandles.Contains(window.Handle)).ToList();
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
