using System.IO;

namespace DesktopTuner;

public static class NativeShellContextMenuPolicy
{
    public static IReadOnlyList<string> NormalizeShellSelection(IEnumerable<string> parsingNames)
    {
        ArgumentNullException.ThrowIfNull(parsingNames);
        var normalized = parsingNames
            .Select(name => name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("Select at least one valid Windows Shell item.", nameof(parsingNames));
        return normalized;
    }

    public static IReadOnlyList<string> NormalizeSelection(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var provided = paths.ToArray();
        if (provided.Length == 0 || provided.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Select at least one valid file or folder.", nameof(paths));
        var normalized = provided
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0) throw new ArgumentException("Select at least one file or folder.", nameof(paths));

        var parent = Path.GetDirectoryName(normalized[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parent)) throw new ArgumentException("Drive roots do not have a filesystem context menu.", nameof(paths));
        if (normalized.Any(path => !string.Equals(
            Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            parent,
            StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Windows can show a shared context menu only for items in the same folder.", nameof(paths));

        return normalized;
    }
}
