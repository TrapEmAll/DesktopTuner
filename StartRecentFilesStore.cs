using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

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
                .Select(info => new AppEntry(GetDisplayName(info), info.FullName))
                .ToList();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public bool IsRecentShortcut(string path) => IsPathInRecentDirectory(path)
        && string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase);

    public bool TryRemove(string path)
    {
        if (!IsRecentShortcut(path) || !File.Exists(path)) return false;
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool IsPathInRecentDirectory(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var recentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_recentDirectory)) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(recentRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    private static string GetDisplayName(FileInfo shortcut)
    {
        var fallback = Path.GetFileNameWithoutExtension(shortcut.Name);
        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return fallback;
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return fallback;
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcut.FullName);
            if (shortcutObject is null) return fallback;
            dynamic recentShortcut = shortcutObject;
            var targetPath = (string)recentShortcut.TargetPath;
            return string.IsNullOrWhiteSpace(targetPath)
                ? fallback
                : Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } targetName
                    ? targetName
                    : fallback;
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException or NotSupportedException)
        {
            Trace.TraceWarning($"Could not resolve recent shortcut '{shortcut.FullName}': {ex.Message}");
            return fallback;
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
