namespace DesktopTuner;

public static class TaskbarKeyboardNavigationPolicy
{
    public static IReadOnlyList<TaskbarDisplay> OrderDisplays(IEnumerable<TaskbarDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        return displays
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.Top)
            .ThenBy(display => display.Left)
            .ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static TaskbarDisplay? SelectForForeground(IEnumerable<TaskbarDisplay> displays, string? foregroundDeviceName)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var ordered = OrderDisplays(displays);
        if (ordered.Count == 0) return null;
        return !string.IsNullOrWhiteSpace(foregroundDeviceName)
            ? ordered.FirstOrDefault(display => string.Equals(display.DeviceName, foregroundDeviceName, StringComparison.OrdinalIgnoreCase))
                ?? ordered.FirstOrDefault(display => display.IsPrimary)
                ?? ordered[0]
            : ordered.FirstOrDefault(display => display.IsPrimary) ?? ordered[0];
    }

    public static int? GetAdjacentIndex(int currentIndex, int itemCount, bool forward)
    {
        if (itemCount < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
        if (itemCount == 0) return null;
        if (currentIndex < -1 || currentIndex >= itemCount) throw new ArgumentOutOfRangeException(nameof(currentIndex));

        var origin = currentIndex < 0 ? forward ? itemCount - 1 : 0 : currentIndex;
        return (origin + (forward ? 1 : -1) + itemCount) % itemCount;
    }
}
