using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public sealed record ExplorerFolderViewPreference(
    ExplorerViewMode ViewMode,
    ExplorerSortColumn SortColumn,
    bool SortAscending,
    ExplorerColumnWidths? ColumnWidths = null);

public sealed record ExplorerColumnWidths(
    double Name = 360,
    double DateModified = 155,
    double Type = 130,
    double Size = 105,
    double DateCreated = 155,
    double DateAccessed = 155);

public sealed class ExplorerFolderViewStore
{
    public const int MaximumFolderPreferences = 2048;
    private readonly string _path;

    public ExplorerFolderViewStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopTuner", "explorer-folder-views.json");
    }

    public ExplorerFolderViewPreference? Load(string directoryPath)
    {
        var path = NormalizePath(directoryPath);
        if (path is null) return null;
        return ReadAll().GetValueOrDefault(path);
    }

    public bool Save(string directoryPath, ExplorerFolderViewPreference preference)
    {
        var path = NormalizePath(directoryPath);
        if (path is null || !IsValid(preference)) return false;

        var preferences = ReadAll();
        if (!preferences.ContainsKey(path) && preferences.Count == MaximumFolderPreferences)
            preferences.Remove(preferences.Keys.First());
        preferences[path] = preference;

        var directory = Path.GetDirectoryName(_path) ?? Environment.CurrentDirectory;
        var temporaryPath = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not save Explorer folder view preferences to '{_path}': {ex.Message}");
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Could not remove temporary Explorer folder view file '{temporaryPath}': {ex.Message}");
                }
            }
        }
    }

    private Dictionary<string, ExplorerFolderViewPreference> ReadAll()
    {
        if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var serialized = JsonSerializer.Deserialize<Dictionary<string, ExplorerFolderViewPreference>>(File.ReadAllText(_path));
            if (serialized is null) return new(StringComparer.OrdinalIgnoreCase);

            var normalized = new Dictionary<string, ExplorerFolderViewPreference>(StringComparer.OrdinalIgnoreCase);
            foreach (var (rawPath, preference) in serialized)
            {
                var path = NormalizePath(rawPath);
                if (path is null || !IsValid(preference) || normalized.ContainsKey(path)) continue;
                normalized.Add(path, preference);
                if (normalized.Count == MaximumFolderPreferences) break;
            }
            return normalized;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not read Explorer folder view preferences from '{_path}': {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool IsValid(ExplorerFolderViewPreference preference) =>
        preference is not null && Enum.IsDefined(preference.ViewMode) && Enum.IsDefined(preference.SortColumn)
        && (preference.ColumnWidths is null || IsValid(preference.ColumnWidths));

    private static bool IsValid(ExplorerColumnWidths widths) =>
        IsValid(widths.Name) && IsValid(widths.DateModified) && IsValid(widths.Type) && IsValid(widths.Size)
        && IsValid(widths.DateCreated) && IsValid(widths.DateAccessed);

    private static bool IsValid(double width) => double.IsFinite(width) && width is >= 48 and <= 4096;

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
