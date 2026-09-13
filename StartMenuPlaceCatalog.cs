using System.IO;

namespace DesktopTuner;

public sealed record StartMenuPlace(string Id, string Label);
public sealed record StartMenuPlaceEntry(string Name, string FullPath, bool IsDirectory, bool IsReparsePoint);

public static class StartMenuPlaceCatalog
{
    private const int MaximumScannedChildren = 256;

    public static IReadOnlyList<StartMenuPlace> DropdownPlaces { get; } =
    [
        new("documents", "Documents"),
        new("downloads", "Downloads"),
        new("music", "Music"),
        new("pictures", "Pictures"),
        new("videos", "Videos")
    ];

    public static IReadOnlyList<StartMenuPlace> AdditionalPlaces { get; } =
    [
        new("computer", "This PC"),
        new("control-panel", "Control Panel"),
        new("network", "Network"),
        new("recent", "Recent items")
    ];

    public static string ResolveTarget(string id) => id switch
    {
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "computer" => "shell:MyComputerFolder",
        "control-panel" => "control.exe",
        "network" => "shell:NetworkPlacesFolder",
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "recent" => Environment.GetFolderPath(Environment.SpecialFolder.Recent),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown Start menu place.")
    };

    public static IReadOnlyList<StartMenuPlaceEntry> ReadChildren(string directoryPath, int maximum = 12)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (maximum == 0 || !Directory.Exists(directoryPath)) return [];

        var children = new List<(StartMenuPlaceEntry Entry, DateTime Modified)>();
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directoryPath).Take(MaximumScannedChildren))
            {
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    var modified = isDirectory ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path);
                    children.Add((new StartMenuPlaceEntry(Path.GetFileName(path), path, isDirectory,
                        (attributes & FileAttributes.ReparsePoint) != 0), modified));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
                {
                    System.Diagnostics.Trace.TraceWarning($"Could not read Start place entry '{path}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not read Start place '{directoryPath}': {ex.Message}");
            return [];
        }

        return children
            .OrderByDescending(item => item.Entry.IsDirectory)
            .ThenByDescending(item => item.Modified)
            .ThenBy(item => item.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximum)
            .Select(item => item.Entry)
            .ToArray();
    }
}
