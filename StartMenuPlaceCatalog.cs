using System.IO;

namespace DesktopTuner;

public sealed record StartMenuPlace(string Id, string Label);
public sealed record StartMenuPlaceEntry(string Name, string FullPath, bool IsDirectory, bool IsReparsePoint);
public sealed record StartMenuPlacePreferences(List<string>? Order = null, List<string>? Visible = null);

public static class StartMenuPlaceCatalog
{
    private const int MaximumScannedChildren = 256;

    public static IReadOnlyList<StartMenuPlace> DropdownPlaces { get; } =
    [
        new("documents", "Documents"),
        new("downloads", "Downloads"),
        new("music", "Music"),
        new("pictures", "Pictures"),
        new("videos", "Videos"),
        new("libraries", "Libraries"),
        new("devices-printers", "Devices and Printers"),
        new("network", "Network")
    ];

    public static IReadOnlyList<StartMenuPlace> AdditionalPlaces { get; } =
    [
        new("user-profile", "User profile"),
        new("computer", "This PC"),
        new("recycle-bin", "Recycle Bin"),
        new("control-panel", "Control Panel"),
        new("recent", "Recent items"),
        new("run", "Run...")
    ];

    public static IReadOnlyList<StartMenuPlace> AllPlaces { get; } = DropdownPlaces.Concat(AdditionalPlaces).ToArray();

    public static StartMenuPlacePreferences Normalize(StartMenuPlacePreferences? preferences)
    {
        var validIds = AllPlaces.Select(place => place.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = (preferences?.Order ?? [])
            .Where(validIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Concat(AllPlaces.Select(place => place.Id).Where(id => !(preferences?.Order ?? []).Contains(id, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        var visibleSet = preferences?.Visible is null
            ? order.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : preferences.Visible.Where(validIds.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (preferences?.Visible is not null
            && !(preferences.Order ?? []).Contains("recycle-bin", StringComparer.OrdinalIgnoreCase))
            visibleSet.Add("recycle-bin");
        return new StartMenuPlacePreferences(order, order.Where(visibleSet.Contains).ToList());
    }

    public static StartMenuPlacePreferences Move(StartMenuPlacePreferences? preferences, string placeId, int offset)
    {
        var normalized = Normalize(preferences);
        if (offset is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(offset));
        var index = normalized.Order!.FindIndex(id => string.Equals(id, placeId, StringComparison.OrdinalIgnoreCase));
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= normalized.Order.Count) return normalized;
        (normalized.Order[index], normalized.Order[destination]) = (normalized.Order[destination], normalized.Order[index]);
        return Normalize(normalized);
    }

    public static string ResolveTarget(string id) => id switch
    {
        "user-profile" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "computer" => "shell:MyComputerFolder",
        "libraries" => "shell:Libraries",
        "devices-printers" => "shell:PrintersFolder",
        "recycle-bin" => "shell:RecycleBinFolder",
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
        if (maximum == 0) return [];
        if (DesktopShellNamespaceCatalog.IsShellNamespaceLocation(directoryPath))
        {
            try
            {
                return DesktopShellNamespaceCatalog.ReadChildren(directoryPath)
                    .Take(maximum)
                    .Select(entry => new StartMenuPlaceEntry(entry.Name, entry.ParsingName, entry.IsFolder, IsReparsePoint: false))
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                System.Diagnostics.Trace.TraceWarning($"Could not read Start Shell place '{directoryPath}': {ex.Message}");
                return [];
            }
        }
        if (!Directory.Exists(directoryPath)) return [];

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

    public static bool CanExpand(StartMenuPlaceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsDirectory && !entry.IsReparsePoint;
    }
}
