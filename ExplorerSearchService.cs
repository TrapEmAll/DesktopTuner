using System.IO;
using System.Diagnostics;

namespace DesktopTuner;

public sealed record ExplorerSearchResult(IReadOnlyList<ExplorerEntry> Entries, int SkippedItems);

public static class ExplorerSearchService
{
    public static Task<ExplorerSearchResult> SearchAsync(string rootDirectory, string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var searchTerm = query.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(searchTerm);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"The search folder '{root}' does not exist.");
        return Task.Run(() => Search(root, searchTerm, cancellationToken), cancellationToken);
    }

    private static ExplorerSearchResult Search(string root, string query, CancellationToken cancellationToken)
    {
        var results = new List<ExplorerEntry>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);
        var skippedItems = 0;

        while (pendingDirectories.TryPop(out var currentDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> children;
            try { children = Directory.EnumerateFileSystemEntries(currentDirectory).ToArray(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                skippedItems++;
                Trace.TraceWarning($"Skipping inaccessible Explorer search folder '{currentDirectory}': {ex.Message}");
                continue;
            }

            foreach (var path in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExplorerEntry entry;
                try { entry = ReadEntry(path); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
                {
                    skippedItems++;
                    Trace.TraceWarning($"Skipping Explorer search item '{path}': {ex.Message}");
                    continue;
                }

                if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) results.Add(entry);
                if (entry.IsDirectory && !entry.IsReparsePoint) pendingDirectories.Push(entry.FullPath);
            }
        }

        return new ExplorerSearchResult(results
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.FullPath, StringComparer.CurrentCultureIgnoreCase)
            .ToList(), skippedItems);
    }

    private static ExplorerEntry ReadEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var modified = File.GetLastWriteTime(path);
        var entry = isDirectory
            ? new ExplorerEntry(Path.GetFileName(path), path, true, false, null, modified)
            : new ExplorerEntry(Path.GetFileName(path), path, false, false, new FileInfo(path).Length, modified);
        return entry with { IsReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0 };
    }
}
