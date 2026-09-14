using System.IO;
using System.Security;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DesktopTuner;

public sealed class DesktopHostItem(string name, string fullPath, bool isDirectory, bool isShellNamespace = false) : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
    public bool IsDirectory { get; } = isDirectory;
    public bool IsShellNamespace { get; } = isShellNamespace;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? Icon => IsShellNamespace ? TaskbarIconService.LoadNamespaceIcon(FullPath) : TaskbarIconService.LoadIcon(FullPath);
    public bool CanShowNativeContextMenu => !IsShellNamespace && (File.Exists(FullPath) || Directory.Exists(FullPath));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        entries.TryAdd("shell:MyComputerFolder", new DesktopHostItem("This PC", "shell:MyComputerFolder", true, isShellNamespace: true));
        entries.TryAdd("shell:RecycleBinFolder", new DesktopHostItem("Recycle Bin", "shell:RecycleBinFolder", true, isShellNamespace: true));
        return entries.Values.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
