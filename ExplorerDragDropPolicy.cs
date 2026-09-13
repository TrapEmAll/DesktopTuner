using System.IO;

namespace DesktopTuner;

public static class ExplorerDragDropPolicy
{
    public static bool ResolveMove(IEnumerable<string> sourcePaths, string destinationDirectory, bool controlPressed, bool shiftPressed)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (controlPressed) return false;
        if (shiftPressed) return true;

        var destinationRoot = Path.GetPathRoot(Path.GetFullPath(destinationDirectory));
        return sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .All(path => string.Equals(Path.GetPathRoot(path), destinationRoot, StringComparison.OrdinalIgnoreCase));
    }
}
