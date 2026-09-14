using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class DesktopShellNamespaceCatalog
{
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
                    var parsingName = parsingNameValue.ToUpperInvariant() switch
                    {
                        "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" => "shell:MyComputerFolder",
                        "::{645FF040-5081-101B-9F08-00AA002F954E}" => "shell:RecycleBinFolder",
                        _ => parsingNameValue
                    };
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
}
