using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace DesktopTuner;

public static class ExplorerHomeService
{
    private const int MaximumRecentFiles = 50;

    public static IReadOnlyList<ExplorerEntry> ReadHomeFiles(bool showRecentItems, bool showHiddenItems) =>
        showRecentItems ? SelectHomeFiles(ReadRecentFiles(showHiddenItems), showRecentItems, showHiddenItems) : [];

    public static IReadOnlyList<ExplorerEntry> SelectHomeFiles(IEnumerable<ExplorerEntry> entries, bool showRecentItems, bool showHiddenItems)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return showRecentItems ? SelectRecentFiles(entries, showHiddenItems) : [];
    }

    public static IReadOnlyList<ExplorerEntry> SelectRecentFiles(IEnumerable<ExplorerEntry> entries, bool showHiddenItems, int maximum = MaximumRecentFiles)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        return entries
            .Where(entry => !entry.IsDirectory && (showHiddenItems || !entry.IsHidden))
            .DistinctBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(entry => entry.RecentAccessed ?? entry.Modified)
            .Take(maximum)
            .ToArray();
    }

    public static IReadOnlyList<ExplorerEntry> ReadRecentFiles(bool showHiddenItems)
    {
        var recentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (!Directory.Exists(recentDirectory)) return [];

        object? shellObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return [];
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return [];
            dynamic shell = shellObject;

            var recentEntries = new List<ExplorerEntry>();
            var shortcuts = Directory.EnumerateFiles(recentDirectory, "*.lnk")
                .Select(path => (Path: path, Modified: File.GetLastWriteTime(path)))
                .OrderByDescending(item => item.Modified)
                .Take(MaximumRecentFiles * 3);
            foreach (var shortcut in shortcuts)
            {
                object? shortcutObject = null;
                try
                {
                    shortcutObject = shell.CreateShortcut(shortcut.Path);
                    dynamic recentShortcut = shortcutObject;
                    var targetPath = (string)recentShortcut.TargetPath;
                    if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath)) continue;

                    var info = new FileInfo(targetPath);
                    var attributes = info.Attributes;
                    var entry = new ExplorerEntry(info.Name, info.FullName, false, false, info.Length, info.LastWriteTime)
                    {
                        IsHidden = (attributes & FileAttributes.Hidden) != 0,
                        IsSystem = (attributes & FileAttributes.System) != 0,
                        RecentAccessed = shortcut.Modified,
                        Created = info.CreationTime,
                        Accessed = info.LastAccessTime
                    };
                    if (!entry.IsSystem && (showHiddenItems || !entry.IsHidden)) recentEntries.Add(entry);
                }
                catch (Exception ex) when (ex is COMException or RuntimeBinderException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    Trace.TraceWarning($"Could not read recent shortcut '{shortcut.Path}': {ex.Message}");
                }
                finally
                {
                    ReleaseComObject(shortcutObject);
                }
            }

            return SelectRecentFiles(recentEntries, showHiddenItems);
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            Trace.TraceWarning($"Could not load recent files for Explorer Home: {ex.Message}");
            return [];
        }
        finally
        {
            ReleaseComObject(shellObject);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
