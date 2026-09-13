using System.Diagnostics;
using System.IO;
using System.Security;

namespace DesktopTuner;

public sealed record ExplorerNavigationDirectory(string Name, string Path);
public sealed record ExplorerNavigationResult(IReadOnlyList<ExplorerNavigationDirectory> Directories, string? Error);

public static class ExplorerNavigationService
{
    public static ExplorerNavigationResult ReadDirectories(string path, bool showHiddenItems, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directories = new List<ExplorerNavigationDirectory>();
            foreach (var directoryPath in Directory.EnumerateDirectories(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(directoryPath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.System) != 0) continue;
                    if (!showHiddenItems && (attributes & FileAttributes.Hidden) != 0) continue;
                    var name = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (name.Length > 0) directories.Add(new(name, directoryPath));
                }
                catch (Exception ex) when (IsExpectedFileSystemException(ex))
                {
                    Trace.TraceWarning("Could not inspect folder in Explorer navigation tree {0}: {1}", directoryPath, ex.Message);
                }
            }

            return new(directories.OrderBy(directory => directory.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Trace.TraceWarning("Could not read Explorer navigation folders at {0}: {1}", path, ex.Message);
            return new([], ex.Message);
        }
    }

    public static ExplorerNavigationResult ReadDriveRoots()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Select(drive => new ExplorerNavigationDirectory(GetDriveLabel(drive), drive.RootDirectory.FullName))
                .OrderBy(drive => drive.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            return new(drives, null);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Trace.TraceWarning("Could not list available drives in Explorer navigation tree: {0}", ex.Message);
            return new([], ex.Message);
        }
    }

    private static string GetDriveLabel(DriveInfo drive)
    {
        var root = drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            var label = drive.IsReady ? drive.VolumeLabel : string.Empty;
            return string.IsNullOrWhiteSpace(label) ? $"Drive ({root})" : $"{label} ({root})";
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Trace.TraceWarning("Could not read volume label for {0}: {1}", root, ex.Message);
            return $"Drive ({root})";
        }
    }

    private static bool IsExpectedFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException;
}
