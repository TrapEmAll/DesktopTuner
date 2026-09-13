using System.IO;

namespace DesktopTuner;

public sealed record TaskbarWindowGroup(string Label, string ApplicationName, IReadOnlyList<RunningWindow> Windows)
{
    public string ToolTip => string.Join(Environment.NewLine, Windows.Select(window => window.Title));
}

public static class TaskbarWindowGrouping
{
    public static IReadOnlyList<TaskbarWindowGroup> Create(IEnumerable<RunningWindow> windows, TaskbarGroupingMode mode, int buttonCapacity, IEnumerable<PinnedTaskbarApp>? pinnedApps = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));

        var entries = windows.ToList();
        var remaining = entries.Where(window => pinnedApps is null || !pinnedApps.Any(app => MatchesPinnedApp(app, window))).ToList();
        if (mode == TaskbarGroupingMode.Never || (mode == TaskbarGroupingMode.WhenFull && entries.Count <= Math.Max(1, buttonCapacity)))
            return remaining.Select(window => CreateGroup([window])).ToList();

        return remaining
            .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateGroup(group.ToList()))
            .ToList();
    }

    public static bool MatchesPinnedApp(PinnedTaskbarApp app, RunningWindow window)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(window);
        if (app.IsDirectory || string.IsNullOrWhiteSpace(app.ExecutablePath) || string.IsNullOrWhiteSpace(window.ExecutablePath)) return false;
        return string.Equals(NormalizePath(app.ExecutablePath), NormalizePath(window.ExecutablePath), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

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
