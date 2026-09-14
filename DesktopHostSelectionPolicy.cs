namespace DesktopTuner;

public sealed record DesktopHostSelection(IReadOnlySet<string> Paths, string? AnchorPath);

public enum DesktopHostNavigationDirection
{
    Left,
    Right,
    Up,
    Down
}

public static class DesktopHostSelectionPolicy
{
    public static DesktopHostItem? FindAdjacentItem(
        IReadOnlyList<DesktopHostItem> items,
        string currentPath,
        DesktopHostNavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));

        var current = items.FirstOrDefault(item => string.Equals(item.FullPath, currentPath, StringComparison.OrdinalIgnoreCase));
        if (current is null) return null;

        var horizontal = direction is DesktopHostNavigationDirection.Left or DesktopHostNavigationDirection.Right;
        var sign = direction is DesktopHostNavigationDirection.Left or DesktopHostNavigationDirection.Up ? -1 : 1;
        var currentPrimary = horizontal ? current.Left : current.Top;
        var currentCross = horizontal ? current.Top : current.Left;
        return items
            .Where(item => !ReferenceEquals(item, current))
            .Select(item =>
            {
                var primary = horizontal ? item.Left : item.Top;
                var cross = horizontal ? item.Top : item.Left;
                var forwardDistance = (primary - currentPrimary) * sign;
                var crossDistance = Math.Abs(cross - currentCross);
                return (Item: item, ForwardDistance: forwardDistance, CrossDistance: crossDistance);
            })
            .Where(candidate => candidate.ForwardDistance > 0)
            .OrderBy(candidate => candidate.ForwardDistance + candidate.CrossDistance * 2)
            .ThenBy(candidate => candidate.CrossDistance)
            .ThenBy(candidate => candidate.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    public static DesktopHostSelection Select(
        IReadOnlyList<DesktopHostItem> items,
        string path,
        bool controlPressed,
        bool shiftPressed,
        string? anchorPath)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var targetIndex = IndexOf(items, path);
        if (targetIndex < 0) return new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), anchorPath);

        var selected = items.Where(item => item.IsSelected)
            .Select(item => item.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var anchorIndex = string.IsNullOrWhiteSpace(anchorPath) ? -1 : IndexOf(items, anchorPath);
        if (shiftPressed && anchorIndex >= 0)
        {
            if (!controlPressed) selected.Clear();
            var start = Math.Min(anchorIndex, targetIndex);
            var end = Math.Max(anchorIndex, targetIndex);
            for (var index = start; index <= end; index++) selected.Add(items[index].FullPath);
            return new(selected, anchorPath);
        }

        if (controlPressed)
        {
            if (!selected.Add(path)) selected.Remove(path);
            return new(selected, path);
        }

        return new(new HashSet<string>([path], StringComparer.OrdinalIgnoreCase), path);
    }

    public static IReadOnlySet<string> SelectAll(IEnumerable<DesktopHostItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(item => item.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static DesktopHostSelection PreserveSelectionForContextMenu(IReadOnlyList<DesktopHostItem> items, string path)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!items.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            return new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), null);

        var selected = items.Where(item => item.IsSelected).Select(item => item.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!selected.Contains(path)) selected = new HashSet<string>([path], StringComparer.OrdinalIgnoreCase);
        return new(selected, path);
    }

    private static int IndexOf(IReadOnlyList<DesktopHostItem> items, string path)
    {
        for (var index = 0; index < items.Count; index++)
            if (string.Equals(items[index].FullPath, path, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }
}
