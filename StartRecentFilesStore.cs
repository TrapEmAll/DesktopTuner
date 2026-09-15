using System.IO;

namespace DesktopTuner;

public sealed class StartRecentFilesStore
{
    public const int MaximumEntries = 12;

    private readonly string _recentDirectory;

    public StartRecentFilesStore(string? recentDirectory = null)
    {
        _recentDirectory = Path.GetFullPath(recentDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Recent"));
    }

    public IReadOnlyList<AppEntry> ReadRecentFiles(IEnumerable<string>? excludedShortcutPaths = null, int maximumEntries = MaximumEntries)
    {
        var excluded = new HashSet<string>(excludedShortcutPaths ?? [], StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_recentDirectory)) return [];

        try
        {
            return Directory.EnumerateFiles(_recentDirectory, "*.lnk", SearchOption.TopDirectoryOnly)
                .Where(path => !excluded.Contains(path))
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(Math.Clamp(maximumEntries, 0, MaximumEntries))
                .Select(info => new AppEntry(Path.GetFileNameWithoutExtension(info.Name), info.FullName))
                .ToList();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
