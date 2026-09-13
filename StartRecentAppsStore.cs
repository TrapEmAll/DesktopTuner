using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public sealed class StartRecentAppsStore
{
    public const int MaximumEntries = 12;

    private readonly string _path;

    public StartRecentAppsStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopTuner",
            "recent-start-apps.json"));
    }

    public IReadOnlyList<AppEntry> Resolve(IEnumerable<AppEntry> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var appsByPath = catalog
            .Where(app => !string.IsNullOrWhiteSpace(app.ShortcutPath))
            .DistinctBy(app => app.ShortcutPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(app => app.ShortcutPath, StringComparer.OrdinalIgnoreCase);
        return LoadPaths()
            .Where(appsByPath.ContainsKey)
            .Select(path => appsByPath[path])
            .ToList();
    }

    public IReadOnlyList<string> LoadPaths()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? [];
            return Normalize(paths);
        }
        catch (JsonException) { return []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public bool TryRecordLaunch(AppEntry app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(app.ShortcutPath)) return false;
        var paths = new List<string> { app.ShortcutPath };
        paths.AddRange(LoadPaths().Where(path => !string.Equals(path, app.ShortcutPath, StringComparison.OrdinalIgnoreCase)));
        return TrySave(Normalize(paths));
    }

    public bool TryClear() => TrySave([]);

    private bool TrySave(IReadOnlyList<string> paths)
    {
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(paths));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string>? paths) => (paths ?? [])
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaximumEntries)
        .ToList();
}
