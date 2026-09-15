using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DesktopTuner;

public sealed record DesktopShellNamespaceEntry(string Name, string ParsingName, bool IsFolder) : INotifyPropertyChanged
{
    private bool _isRenaming;
    private string _renameText = Name;
    private bool _canRename;
    private bool _renameCapabilityChecked;
    private bool _isCut;

    public ImageSource? Icon { get; init; }
    public bool CanRename
    {
        get => _canRename;
        private set { if (_canRename == value) return; _canRename = value; OnPropertyChanged(); }
    }
    public bool RenameCapabilityChecked
    {
        get => _renameCapabilityChecked;
        private set { if (_renameCapabilityChecked == value) return; _renameCapabilityChecked = value; OnPropertyChanged(); }
    }
    public bool IsRenaming
    {
        get => _isRenaming;
        set { if (_isRenaming == value) return; _isRenaming = value; OnPropertyChanged(); }
    }
    public string RenameText
    {
        get => _renameText;
        set { if (string.Equals(_renameText, value, StringComparison.Ordinal)) return; _renameText = value; OnPropertyChanged(); }
    }
    public string Type => IsFolder ? "Folder" : "Item";
    public bool IsCut
    {
        get => _isCut;
        set { if (_isCut == value) return; _isCut = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetRenameCapability(bool canRename)
    {
        CanRename = canRename;
        RenameCapabilityChecked = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record DesktopShellNamespaceSearchResult(
    IReadOnlyList<DesktopShellNamespaceEntry> Entries,
    int SkippedItems,
    int SkippedContentItems);

public static class DesktopShellNamespaceCatalog
{
    public static string GetFriendlyName(string parsingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
        var value = parsingName.Trim();
        return value.ToLowerInvariant() switch
        {
            "shell:mycomputerfolder" => "This PC",
            "shell:recyclebinfolder" => "Recycle Bin",
            "shell:networkplacesfolder" => "Network",
            "shell:controlpanelfolder" => "Control Panel",
            var normalized when normalized.StartsWith("shell:", StringComparison.Ordinal) => value[6..],
            _ => value
        };
    }

    public static bool IsShellNamespaceLocation(string? parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return false;
        var value = parsingName.Trim();
        return value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("::{", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCompanionExplorerLocation(string parsingName) =>
        string.Equals(parsingName, "shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(parsingName, "shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<DesktopShellNamespaceEntry> ReadChildren(string parsingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
        if (!IsShellNamespaceLocation(parsingName) && !Directory.Exists(parsingName))
            throw new ArgumentException("The location is not a Windows Shell namespace or an existing folder.", nameof(parsingName));

        object? shell = null;
        object? folder = null;
        object? items = null;
        var entries = new List<DesktopShellNamespaceEntry>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
            if (shellType is null) return [];
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return [];
            dynamic shellDispatch = shell;
            folder = shellDispatch.Namespace(parsingName);
            if (folder is null) return [];
            dynamic folderDispatch = folder;
            items = folderDispatch.Items();
            if (items is null) return [];

            foreach (dynamic item in (dynamic)items)
            {
                object? itemObject = item;
                try
                {
                    dynamic shellItem = itemObject!;
                    var name = Convert.ToString((object?)shellItem.Name)?.Trim();
                    var rawPath = Convert.ToString((object?)shellItem.Path)?.Trim();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rawPath)) continue;
                    var path = NormalizeParsingName(rawPath);
                    if (!paths.Add(path)) continue;
                    entries.Add(new DesktopShellNamespaceEntry(name, path, Convert.ToBoolean(shellItem.IsFolder)));
                }
                catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or FormatException)
                {
                    Trace.TraceWarning($"Could not read a Windows Shell namespace child: {ex.Message}");
                }
                finally
                {
                    if (itemObject is not null && Marshal.IsComObject(itemObject)) Marshal.ReleaseComObject(itemObject);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or FormatException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.TraceWarning($"Could not enumerate Windows Shell location '{parsingName}': {ex.Message}");
            return [];
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }
        return entries.OrderByDescending(entry => entry.IsFolder).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static Task<IReadOnlyList<DesktopShellNamespaceEntry>> ReadChildrenAsync(string parsingName) =>
        RunStaAsync(() => ReadChildren(parsingName));

    public static Task<DesktopShellNamespaceSearchResult> SearchAsync(string location, string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (!IsShellNamespaceLocation(location) && !Directory.Exists(location))
            throw new ArgumentException("The location is not a Windows Shell namespace or an existing folder.", nameof(location));

        var criteria = ExplorerSearchQuery.Parse(query.Trim());
        return RunStaAsync(() => Search(location, criteria, cancellationToken));
    }

    private static DesktopShellNamespaceSearchResult Search(string root, ExplorerSearchQuery criteria, CancellationToken cancellationToken)
    {
        const int maximumDepth = 128;
        var results = new List<DesktopShellNamespaceEntry>();
        var pending = new Stack<(string Location, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push((root, 0));
        visited.Add(root);
        var skippedItems = 0;
        var skippedContentItems = 0;

        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var child in ReadChildren(current.Location))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = CreateSearchEntry(child);
                if (criteria.Matches(entry))
                {
                    if (!criteria.RequiresContentMatch) results.Add(child);
                    else if (!child.IsFolder)
                    {
                        if (!File.Exists(child.ParsingName)) skippedContentItems++;
                        else if (!ExplorerContentSearch.CanSearch(child.ParsingName, entry.Length)) skippedContentItems++;
                        else
                        {
                            try
                            {
                                if (criteria.MatchesContent(child.ParsingName, entry.Length, cancellationToken)) results.Add(child);
                            }
                            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
                            {
                                skippedItems++;
                                Trace.TraceWarning($"Skipping Shell namespace content-search item '{child.ParsingName}': {ex.Message}");
                            }
                        }
                    }
                    else skippedContentItems++;
                }

                if (!child.IsFolder) continue;
                if (current.Depth >= maximumDepth)
                {
                    skippedItems++;
                    continue;
                }
                if (IsFileSystemReparsePoint(child.ParsingName)) continue;
                if (visited.Add(child.ParsingName)) pending.Push((child.ParsingName, current.Depth + 1));
            }
        }

        var sorted = results
            .OrderByDescending(entry => entry.IsFolder)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.ParsingName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return new DesktopShellNamespaceSearchResult(sorted, skippedItems, skippedContentItems);
    }

    private static ExplorerEntry CreateSearchEntry(DesktopShellNamespaceEntry entry)
    {
        long? length = null;
        var modified = DateTime.MinValue;
        var searchName = entry.Name;
        if (!IsShellNamespaceLocation(entry.ParsingName))
        {
            try
            {
                modified = File.GetLastWriteTime(entry.ParsingName);
                if (!entry.IsFolder)
                {
                    var extension = Path.GetExtension(entry.ParsingName);
                    if (extension.Length > 0 && !searchName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) searchName += extension;
                    if (File.Exists(entry.ParsingName)) length = new FileInfo(entry.ParsingName).Length;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
            {
                Trace.TraceWarning($"Could not read Shell namespace search metadata for '{entry.ParsingName}': {ex.Message}");
            }
        }
        return new ExplorerEntry(searchName, entry.ParsingName, entry.IsFolder, false, length, modified);
    }

    private static bool IsFileSystemReparsePoint(string parsingName)
    {
        if (IsShellNamespaceLocation(parsingName) || !Directory.Exists(parsingName)) return false;
        try { return (File.GetAttributes(parsingName) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
        {
            Trace.TraceWarning($"Could not inspect Shell namespace folder '{parsingName}' before search traversal: {ex.Message}");
            return true;
        }
    }

    public static string? ReadParentLocation(string parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return null;
        object? shell = null;
        object? folder = null;
        object? parentFolder = null;
        object? parentItem = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
            if (shellType is null) return null;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            dynamic shellDispatch = shell;
            folder = shellDispatch.Namespace(parsingName);
            if (folder is null) return null;
            dynamic folderDispatch = folder;
            parentFolder = folderDispatch.ParentFolder;
            if (parentFolder is null) return null;
            dynamic parentDispatch = parentFolder;
            parentItem = parentDispatch.Self;
            if (parentItem is null) return null;
            dynamic shellItem = parentItem;
            var result = Convert.ToString((object?)shellItem.Path)?.Trim();
            return string.IsNullOrWhiteSpace(result) ? null : NormalizeParsingName(result);
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or FormatException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not resolve the parent of Windows Shell location '{parsingName}': {ex.Message}");
            return null;
        }
        finally
        {
            ReleaseComObject(parentItem);
            ReleaseComObject(parentFolder);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }
    }

    public static Task<string?> ReadParentLocationAsync(string parsingName) =>
        RunStaAsync(() => ReadParentLocation(parsingName));

    public static IReadOnlyList<DesktopHostItem> ReadVirtualItems()
    {
        object? shell = null;
        object? desktop = null;
        object? items = null;
        var entries = new List<DesktopHostItem>();
        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
            if (shellType is null) return [];
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return [];
            dynamic shellDispatch = shell;
            desktop = shellDispatch.Namespace(0);
            if (desktop is null) return [];
            dynamic desktopDispatch = desktop;
            items = desktopDispatch.Items();
            if (items is null) return [];

            foreach (dynamic item in (dynamic)items)
            {
                object? itemObject = item;
                try
                {
                    dynamic shellItem = itemObject!;
                    if (Convert.ToBoolean(shellItem.IsFileSystem)) continue;
                    var name = Convert.ToString((object?)shellItem.Name)?.Trim();
                    var parsingNameValue = Convert.ToString((object?)shellItem.Path)?.Trim();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parsingNameValue) || !names.Add(name)) continue;
                    var isFolder = Convert.ToBoolean(shellItem.IsFolder);
                    var parsingName = NormalizeParsingName(parsingNameValue);
                    var canRename = NativeShellContextMenuService.CanRenameShellItem(parsingName);
                    entries.Add(new DesktopHostItem(name, parsingName, isFolder, isShellNamespace: true, shellCanRename: canRename));
                }
                catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or FormatException)
                {
                    Trace.TraceWarning($"Could not read a Windows desktop namespace item: {ex.Message}");
                }
                finally
                {
                    if (itemObject is not null && Marshal.IsComObject(itemObject)) Marshal.ReleaseComObject(itemObject);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or FormatException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not enumerate the Windows desktop Shell namespace: {ex.Message}");
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(desktop);
            ReleaseComObject(shell);
        }

        return entries;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    private static Task<T> RunStaAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception ex) { completion.SetException(ex); }
        })
        {
            IsBackground = true,
            Name = "Desktop Tuner Shell namespace"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static string NormalizeParsingName(string value) => value.ToUpperInvariant() switch
    {
        "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" => "shell:MyComputerFolder",
        "::{645FF040-5081-101B-9F08-00AA002F954E}" => "shell:RecycleBinFolder",
        _ => value
    };
}
