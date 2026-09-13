using System.IO;

namespace DesktopTuner;

public sealed record ExplorerBreadcrumbSegment(string Label, string Path);

public static class ExplorerBreadcrumbPolicy
{
    public static IReadOnlyList<ExplorerBreadcrumbSegment> Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root)) return [];

        var segments = new List<ExplorerBreadcrumbSegment> { new(root, root) };
        var relativePath = Path.GetRelativePath(root, fullPath);
        if (relativePath == ".") return segments;

        var currentPath = root;
        foreach (var part in relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, part);
            segments.Add(new(part, currentPath));
        }

        return segments;
    }
}
