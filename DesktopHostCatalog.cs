using System.IO;
using System.Security;
using System.Windows.Media;

namespace DesktopTuner;

public sealed record DesktopHostItem(string Name, string FullPath, bool IsDirectory, bool IsShellNamespace = false)
{
    public ImageSource? Icon => IsShellNamespace ? TaskbarIconService.LoadNamespaceIcon(FullPath) : TaskbarIconService.LoadIcon(FullPath);
}

public static class DesktopHostCatalog
{
    public static IReadOnlyList<DesktopHostItem> ReadItems(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var entries = new Dictionary<string, DesktopHostItem>(StringComparer.OrdinalIgnoreCase);
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
                        entries.TryAdd(path, new DesktopHostItem(Path.GetFileName(path), path, isDirectory));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
                    {
                        System.Diagnostics.Trace.TraceWarning($"Could not read desktop item '{path}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
            {
                System.Diagnostics.Trace.TraceWarning($"Could not enumerate desktop folder '{root}': {ex.Message}");
            }
        }

        entries.TryAdd("shell:MyComputerFolder", new DesktopHostItem("This PC", "shell:MyComputerFolder", true, IsShellNamespace: true));
        entries.TryAdd("shell:RecycleBinFolder", new DesktopHostItem("Recycle Bin", "shell:RecycleBinFolder", true, IsShellNamespace: true));
        return entries.Values.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
