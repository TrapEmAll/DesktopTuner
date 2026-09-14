using System.IO;

namespace DesktopTuner;

public sealed record DesktopHostDragSelection(
    IReadOnlyList<string> ItemPaths,
    IReadOnlyList<string> FileDropPaths);

public static class DesktopHostDragPolicy
{
    public static DesktopHostDragSelection Resolve(IReadOnlyList<DesktopHostItem> items, DesktopHostItem anchor)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(anchor);

        var currentAnchor = items.FirstOrDefault(item =>
            string.Equals(item.FullPath, anchor.FullPath, StringComparison.OrdinalIgnoreCase));
        if (currentAnchor is null)
            return new([], []);

        var selected = currentAnchor.IsSelected
            ? items.Where(item => item.IsSelected).ToArray()
            : [currentAnchor];
        var ordered = new[] { currentAnchor }
            .Concat(selected.Where(item => !string.Equals(item.FullPath, currentAnchor.FullPath, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var itemPaths = ordered.Select(item => item.FullPath).ToArray();
        var fileDropPaths = ordered
            .Where(item => !item.IsShellNamespace && (File.Exists(item.FullPath) || Directory.Exists(item.FullPath)))
            .Select(item => item.FullPath)
            .ToArray();
        return new DesktopHostDragSelection(itemPaths, fileDropPaths);
    }
}
