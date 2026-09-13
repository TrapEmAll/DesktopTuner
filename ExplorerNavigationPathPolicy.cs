using System.IO;

namespace DesktopTuner;

public static class ExplorerNavigationPathPolicy
{
    public static bool IsSameOrDescendant(string rootPath, string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase)) return true;

        var rootPrefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> GetRelativeSegments(string rootPath, string candidatePath)
    {
        if (!IsSameOrDescendant(rootPath, candidatePath)) return [];
        var relativePath = Path.GetRelativePath(rootPath, candidatePath);
        return relativePath == "."
            ? []
            : relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
    }
}
