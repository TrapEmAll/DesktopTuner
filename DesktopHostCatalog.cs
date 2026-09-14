using System.IO;

namespace DesktopTuner;

public static class DesktopHostCatalog
{
    public static IReadOnlyList<ExplorerEntry> ReadItems(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var entries = new Dictionary<string, ExplorerEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(root))
                {
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        var isDirectory = (attributes & FileAttributes.Directory) != 0;
                        var modified = isDirectory ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path);
                        long? length = isDirectory ? null : new FileInfo(path).Length;
                        entries.TryAdd(path, new ExplorerEntry(Path.GetFileName(path), path, isDirectory, false, length, modified));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return entries.Values.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
