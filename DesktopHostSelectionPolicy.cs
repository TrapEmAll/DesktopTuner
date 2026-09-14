namespace DesktopTuner;

public sealed record DesktopHostSelection(IReadOnlySet<string> Paths, string? AnchorPath);

public static class DesktopHostSelectionPolicy
{
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

    private static int IndexOf(IReadOnlyList<DesktopHostItem> items, string path)
    {
        for (var index = 0; index < items.Count; index++)
            if (string.Equals(items[index].FullPath, path, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }
}
