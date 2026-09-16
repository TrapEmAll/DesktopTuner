namespace DesktopTuner;

public sealed record TaskbarSnapGroup(IReadOnlyList<RunningWindow> Windows)
{
    public bool IsActive => Windows.Any(window => window.IsForeground);
    public string Label => string.Join(" + ", Windows.Select(window => string.IsNullOrWhiteSpace(window.ApplicationName) ? window.Title : window.ApplicationName));
}

public static class TaskbarSnapGroupPolicy
{
    private const double EdgeTolerance = 14;
    private const double MinimumSharedEdge = 0.72;

    public static IReadOnlyList<TaskbarSnapGroup> Detect(IEnumerable<RunningWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var candidates = windows.Where(window => !window.IsMinimized && !window.IsMaximized &&
            !string.IsNullOrWhiteSpace(window.DisplayDeviceName) && window.Bounds.Width > 0 && window.Bounds.Height > 0).ToArray();
        var neighbors = candidates.ToDictionary(window => window.Handle, _ => new HashSet<nint>());
        for (var firstIndex = 0; firstIndex < candidates.Length; firstIndex++)
        for (var secondIndex = firstIndex + 1; secondIndex < candidates.Length; secondIndex++)
        {
            var first = candidates[firstIndex];
            var second = candidates[secondIndex];
            if (!string.Equals(first.DisplayDeviceName, second.DisplayDeviceName, StringComparison.OrdinalIgnoreCase) || !AreAdjacent(first.Bounds, second.Bounds)) continue;
            neighbors[first.Handle].Add(second.Handle);
            neighbors[second.Handle].Add(first.Handle);
        }

        var byHandle = candidates.ToDictionary(window => window.Handle);
        var visited = new HashSet<nint>();
        var groups = new List<TaskbarSnapGroup>();
        foreach (var candidate in candidates)
        {
            if (!visited.Add(candidate.Handle) || neighbors[candidate.Handle].Count == 0) continue;
            var queue = new Queue<nint>([candidate.Handle]);
            var members = new List<RunningWindow>();
            while (queue.Count > 0)
            {
                var handle = queue.Dequeue();
                members.Add(byHandle[handle]);
                foreach (var neighbor in neighbors[handle])
                    if (visited.Add(neighbor)) queue.Enqueue(neighbor);
            }
            if (members.Count > 1) groups.Add(new TaskbarSnapGroup(members));
        }
        return groups;
    }

    public static TaskbarSnapGroup? FindForWindow(TaskbarSnapGroup group, IEnumerable<TaskbarSnapGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(groups);
        var handles = group.Windows.Select(window => window.Handle).ToHashSet();
        return groups.FirstOrDefault(candidate => candidate.Windows.Any(window => handles.Contains(window.Handle)));
    }

    private static bool AreAdjacent(TaskbarBounds first, TaskbarBounds second)
    {
        var horizontalOverlap = Overlap(first.Top, first.Top + first.Height, second.Top, second.Top + second.Height);
        var verticalOverlap = Overlap(first.Left, first.Left + first.Width, second.Left, second.Left + second.Width);
        var horizontalShared = horizontalOverlap / Math.Max(1, Math.Min(first.Height, second.Height));
        var verticalShared = verticalOverlap / Math.Max(1, Math.Min(first.Width, second.Width));
        var sideBySide = Math.Abs((first.Left + first.Width) - second.Left) <= EdgeTolerance || Math.Abs((second.Left + second.Width) - first.Left) <= EdgeTolerance;
        var stacked = Math.Abs((first.Top + first.Height) - second.Top) <= EdgeTolerance || Math.Abs((second.Top + second.Height) - first.Top) <= EdgeTolerance;
        return sideBySide && horizontalShared >= MinimumSharedEdge || stacked && verticalShared >= MinimumSharedEdge;
    }

    private static double Overlap(double firstStart, double firstEnd, double secondStart, double secondEnd) =>
        Math.Max(0, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));
}
