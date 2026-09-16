using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public sealed record ExplorerQuickAccessPin(string Name, string Path);

public static class ExplorerQuickAccessCatalog
{
    public const int MaximumPins = 32;

    public static IReadOnlyList<ExplorerQuickAccessPin> Normalize(IEnumerable<ExplorerQuickAccessPin>? pins)
    {
        if (pins is null) return [];

        var normalized = new List<ExplorerQuickAccessPin>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pin in pins)
        {
            if (pin is null || string.IsNullOrWhiteSpace(pin.Path)) continue;
            string path;
            try
            {
                if (DesktopShellNamespaceCatalog.IsShellNamespaceLocation(pin.Path))
                    path = pin.Path.Trim();
                else
                {
                    if (!System.IO.Path.IsPathFullyQualified(pin.Path)) continue;
                    path = System.IO.Path.GetFullPath(pin.Path);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!paths.Add(path)) continue;
            normalized.Add(new ExplorerQuickAccessPin(
                string.IsNullOrWhiteSpace(pin.Name) ? GetDisplayName(path) : pin.Name.Trim(), path));
            if (normalized.Count == MaximumPins) break;
        }

        return normalized;
    }

    public static string GetDisplayName(string path)
    {
        if (DesktopShellNamespaceCatalog.IsShellNamespaceLocation(path))
            return DesktopShellNamespaceCatalog.GetFriendlyName(path);
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    public static bool Move(IList<ExplorerQuickAccessPin> pins, string path, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(pins);
        var sourceIndex = -1;
        for (var index = 0; index < pins.Count; index++)
        {
            if (!string.Equals(pins[index].Path, path, StringComparison.OrdinalIgnoreCase)) continue;
            sourceIndex = index;
            break;
        }
        return ExplorerTabOrdering.Move(pins, sourceIndex, insertionIndex);
    }

    public static IReadOnlyList<string> GetDroppableFolders(IEnumerable<string>? paths, Func<string, bool> isDirectory)
    {
        ArgumentNullException.ThrowIfNull(isDirectory);
        if (paths is null) return [];

        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in paths)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string path;
            try
            {
                if (!System.IO.Path.IsPathFullyQualified(candidate)) continue;
                path = System.IO.Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!seen.Add(path) || !isDirectory(path)) continue;
            folders.Add(path);
        }

        return folders;
    }

    public static IReadOnlyList<string> GetDroppableShellLocations(IEnumerable<string>? parsingNames)
    {
        if (parsingNames is null) return [];
        return parsingNames
            .Where(DesktopShellNamespaceCatalog.IsShellNamespaceLocation)
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class ExplorerQuickAccessStore
{
    private readonly string _path;

    public ExplorerQuickAccessStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopTuner", "explorer-quick-access.json");
    }

    public IReadOnlyList<ExplorerQuickAccessPin> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            return ExplorerQuickAccessCatalog.Normalize(
                JsonSerializer.Deserialize<List<ExplorerQuickAccessPin>>(File.ReadAllText(_path)));
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning($"Could not read pinned Explorer folders from '{_path}': {ex.Message}");
            return [];
        }
        catch (IOException ex)
        {
            Trace.TraceWarning($"Could not read pinned Explorer folders from '{_path}': {ex.Message}");
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning($"Could not read pinned Explorer folders from '{_path}': {ex.Message}");
            return [];
        }
    }

    public bool Add(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return false;
        var fullPath = DesktopShellNamespaceCatalog.IsShellNamespaceLocation(directoryPath)
            ? directoryPath.Trim()
            : Directory.Exists(directoryPath) ? System.IO.Path.GetFullPath(directoryPath) : null;
        if (fullPath is null) return false;
        var pins = ExplorerQuickAccessCatalog.Normalize(Load());
        if (pins.Count >= ExplorerQuickAccessCatalog.MaximumPins
            || pins.Any(pin => string.Equals(pin.Path, fullPath, StringComparison.OrdinalIgnoreCase))) return false;

        Save([.. pins, new ExplorerQuickAccessPin(ExplorerQuickAccessCatalog.GetDisplayName(fullPath), fullPath)]);
        return true;
    }

    public bool Remove(string directoryPath)
    {
        var pins = ExplorerQuickAccessCatalog.Normalize(Load());
        var updated = pins.Where(pin => !string.Equals(pin.Path, directoryPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (updated.Count == pins.Count) return false;
        Save(updated);
        return true;
    }

    public bool Move(string directoryPath, int insertionIndex)
    {
        var pins = ExplorerQuickAccessCatalog.Normalize(Load()).ToList();
        if (!ExplorerQuickAccessCatalog.Move(pins, directoryPath, insertionIndex)) return false;
        Save(pins);
        return true;
    }

    private void Save(IEnumerable<ExplorerQuickAccessPin> pins)
    {
        var directory = System.IO.Path.GetDirectoryName(_path) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(
                ExplorerQuickAccessCatalog.Normalize(pins), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Could not remove temporary quick access file '{temporaryPath}': {ex.Message}");
                }
            }
        }
    }
}
