using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public sealed record DesktopShellNamespaceEntry(string Name, string ParsingName, bool IsFolder)
{
    public string Type => IsFolder ? "Folder" : "Item";
}

public static class DesktopShellNamespaceCatalog
{
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
                    entries.Add(new DesktopHostItem(name, parsingName, isFolder, isShellNamespace: true));
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
