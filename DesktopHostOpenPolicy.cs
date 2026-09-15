namespace DesktopTuner;

public static class DesktopHostOpenPolicy
{
    public static IReadOnlyList<DesktopHostItem> SelectItems(IEnumerable<DesktopHostItem> items, DesktopHostItem? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var selected = items.Where(item => item.IsSelected).ToArray();
        return selected.Length > 0 || fallback is null ? selected : [fallback];
    }
}
