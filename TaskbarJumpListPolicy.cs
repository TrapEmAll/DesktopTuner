namespace DesktopTuner;

public static class TaskbarJumpListPolicy
{
    public static IReadOnlyList<TaskbarJumpListDestination> NormalizeDestinations(
        IEnumerable<TaskbarJumpListDestination> destinations,
        int maximumCount = 12)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        if (maximumCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (maximumCount == 0) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<TaskbarJumpListDestination>(Math.Min(maximumCount, 12));
        foreach (var destination in destinations)
        {
            if (destination is null || string.IsNullOrWhiteSpace(destination.Name) || string.IsNullOrWhiteSpace(destination.ParsingName)) continue;
            var parsingName = destination.ParsingName.Trim();
            if (!seen.Add(parsingName)) continue;
            normalized.Add(destination with { Name = destination.Name.Trim(), ParsingName = parsingName });
            if (normalized.Count == maximumCount) break;
        }
        return normalized;
    }
}
