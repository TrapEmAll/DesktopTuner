using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace DesktopTuner;

public static class ExplorerRecycleBinService
{
    private const int RecycleBinShellFolder = 10;

    public static IReadOnlyList<ExplorerEntry> ReadEntries()
    {
        object? shellObject = null;
        object? folderObject = null;
        object? itemsObject = null;
        var entries = new List<ExplorerEntry>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) throw new InvalidOperationException("Windows Shell automation is unavailable.");
            shellObject = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create the Windows Shell automation object.");
            dynamic shell = shellObject;
            folderObject = shell.Namespace(RecycleBinShellFolder);
            if (folderObject is null) throw new InvalidOperationException("Windows did not provide the Recycle Bin shell folder.");
            dynamic folder = folderObject;
            itemsObject = folder.Items();
            if (itemsObject is null) return entries;
            dynamic items = itemsObject;
            var count = (int)items.Count;
            for (var index = 0; index < count; index++)
            {
                object? itemObject = null;
                try
                {
                    itemObject = items.Item(index);
                    if (itemObject is null) continue;
                    dynamic item = itemObject;
                    var shellPath = Convert.ToString(item.Path, CultureInfo.InvariantCulture);
                    var name = Convert.ToString(item.Name, CultureInfo.CurrentCulture);
                    if (string.IsNullOrWhiteSpace(shellPath) || string.IsNullOrWhiteSpace(name)) continue;

                    var isDirectory = (bool)item.IsFolder;
                    long? size = isDirectory ? null : Convert.ToInt64(item.Size, CultureInfo.InvariantCulture);
                    var modified = ReadDate(item.ModifyDate) ?? DateTime.MinValue;
                    var deletedFrom = Convert.ToString(item.ExtendedProperty("System.Recycle.DeletedFrom"), CultureInfo.CurrentCulture);
                    var deleted = ReadDate(item.ExtendedProperty("System.Recycle.DateDeleted"));
                    entries.Add(new ExplorerEntry(name, shellPath, isDirectory, false, size, deleted ?? modified)
                    {
                        IsRecycleBinItem = true,
                        ShellItemPath = shellPath,
                        OriginalLocation = deletedFrom,
                        RecycleDeleted = deleted
                    });
                }
                catch (Exception ex) when (IsShellException(ex))
                {
                    Trace.TraceWarning("Could not read a Recycle Bin item: {0}", ex.Message);
                }
                finally
                {
                    ReleaseComObject(itemObject);
                }
            }
            return entries;
        }
        catch (Exception ex) when (IsShellException(ex))
        {
            Trace.TraceError("Could not read the Windows Recycle Bin: {0}", ex);
            throw new InvalidOperationException("Could not read the Windows Recycle Bin.", ex);
        }
        finally
        {
            ReleaseComObject(itemsObject);
            ReleaseComObject(folderObject);
            ReleaseComObject(shellObject);
        }
    }

    public static void Restore(string shellItemPath) => InvokeItemVerb(shellItemPath, "restore");

    public static void DeletePermanently(string shellItemPath) => InvokeItemVerb(shellItemPath, "delete");

    private static void InvokeItemVerb(string shellItemPath, string requestedVerb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellItemPath);
        object? shellObject = null;
        object? folderObject = null;
        object? itemsObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) throw new InvalidOperationException("Windows Shell automation is unavailable.");
            shellObject = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create the Windows Shell automation object.");
            dynamic shell = shellObject;
            folderObject = shell.Namespace(RecycleBinShellFolder);
            if (folderObject is null) throw new InvalidOperationException("Windows did not provide the Recycle Bin shell folder.");
            dynamic folder = folderObject;
            itemsObject = folder.Items();
            if (itemsObject is null) throw new FileNotFoundException("The Recycle Bin item is no longer available.", shellItemPath);
            dynamic items = itemsObject;
            var count = (int)items.Count;
            for (var index = 0; index < count; index++)
            {
                object? itemObject = null;
                object? verbsObject = null;
                try
                {
                    itemObject = items.Item(index);
                    if (itemObject is null) continue;
                    dynamic item = itemObject;
                    var candidatePath = Convert.ToString(item.Path, CultureInfo.InvariantCulture);
                    if (!string.Equals(candidatePath, shellItemPath, StringComparison.OrdinalIgnoreCase)) continue;

                    verbsObject = item.Verbs();
                    if (verbsObject is null) break;
                    dynamic verbs = verbsObject;
                    var verbCount = (int)verbs.Count;
                    for (var verbIndex = 0; verbIndex < verbCount; verbIndex++)
                    {
                        object? verbObject = null;
                        try
                        {
                            verbObject = verbs.Item(verbIndex);
                            if (verbObject is null) continue;
                            dynamic verb = verbObject;
                            var verbName = NormalizeVerbName(Convert.ToString(verb.Name, CultureInfo.CurrentCulture));
                            if (!string.Equals(verbName, requestedVerb, StringComparison.OrdinalIgnoreCase)) continue;
                            verb.DoIt();
                            return;
                        }
                        finally
                        {
                            ReleaseComObject(verbObject);
                        }
                    }
                    // When the Shell localizes the displayed verb name, its automation layer
                    // still accepts the canonical verb identifier (for example, "restore").
                    item.InvokeVerb(requestedVerb);
                    return;
                }
                finally
                {
                    ReleaseComObject(verbsObject);
                    ReleaseComObject(itemObject);
                }
            }
            throw new FileNotFoundException("The Recycle Bin item is no longer available.", shellItemPath);
        }
        catch (Exception ex) when (IsShellException(ex))
        {
            Trace.TraceError("Could not {0} Recycle Bin item '{1}': {2}", requestedVerb, shellItemPath, ex);
            throw new InvalidOperationException($"Could not {requestedVerb} the selected Recycle Bin item.", ex);
        }
        finally
        {
            ReleaseComObject(itemsObject);
            ReleaseComObject(folderObject);
            ReleaseComObject(shellObject);
        }
    }

    private static string NormalizeVerbName(string? name) => string.IsNullOrWhiteSpace(name)
        ? string.Empty
        : name.Trim().TrimStart('&').Replace("&", string.Empty, StringComparison.Ordinal).Replace("...", string.Empty, StringComparison.Ordinal).Trim();

    private static DateTime? ReadDate(object? value)
    {
        if (value is DateTime date) return date;
        return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static bool IsShellException(Exception ex) => ex is COMException or RuntimeBinderException or InvalidCastException or FormatException or OverflowException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException;

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
