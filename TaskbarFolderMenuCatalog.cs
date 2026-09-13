using System.IO;

namespace DesktopTuner;

public sealed record TaskbarFolderMenuEntry(string Name, string FullPath, bool IsDirectory, bool IsReparsePoint);

public static class TaskbarFolderMenuCatalog
{
    public const int MaximumVisibleEntries = 20;
    private const int MaximumScannedEntries = 256;

    public static IReadOnlyList<TaskbarFolderMenuEntry> ReadChildren(string directoryPath, int maximum = MaximumVisibleEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (maximum == 0 || !Directory.Exists(directoryPath)) return [];

        var children = new List<TaskbarFolderMenuEntry>();
        foreach (var path in Directory.EnumerateFileSystemEntries(directoryPath).Take(MaximumScannedEntries))
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                children.Add(new TaskbarFolderMenuEntry(Path.GetFileName(path), path,
                    (attributes & FileAttributes.Directory) != 0, (attributes & FileAttributes.ReparsePoint) != 0));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                System.Diagnostics.Trace.TraceWarning($"Could not read taskbar folder menu entry '{path}': {ex.Message}");
            }
        }

        return children
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximum)
            .ToArray();
    }
}
