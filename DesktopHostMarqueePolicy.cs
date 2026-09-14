using System.Windows;

namespace DesktopTuner;

public sealed record DesktopHostMarqueeItem(string Path, Rect Bounds);

public static class DesktopHostMarqueePolicy
{
    public static Rect CreateRectangle(Point start, Point current) => new(start, current);

    public static IReadOnlySet<string> ResolveSelection(
        IEnumerable<DesktopHostMarqueeItem> items,
        Rect selectionRectangle,
        IEnumerable<string> initiallySelectedPaths,
        bool toggleIntersectedItems)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(initiallySelectedPaths);
        var selection = initiallySelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectionRectangle.IsEmpty) return selection;

        foreach (var item in items)
        {
            if (!selectionRectangle.IntersectsWith(item.Bounds)) continue;
            if (toggleIntersectedItems && !selection.Add(item.Path)) selection.Remove(item.Path);
            else selection.Add(item.Path);
        }

        return selection;
    }
}
