using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class ExplorerPropertiesService
{
    private const uint SHOP_FILEPATH = 0x0002;
    private static readonly Guid IDataObjectId = new("0000010E-0000-0000-C000-000000000046");

    public static bool CanShowProperties(ExplorerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return !entry.IsDrive && (File.Exists(entry.FullPath) || Directory.Exists(entry.FullPath));
    }

    public static bool ShowProperties(ExplorerEntry entry, IntPtr parentWindow)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanShowProperties(entry)) return false;

        return SHObjectProperties(parentWindow, SHOP_FILEPATH, Path.GetFullPath(entry.FullPath), null);
    }

    public static bool CanShowProperties(IEnumerable<ExplorerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var selection = entries.ToList();
        return selection.Count > 0 && selection.All(CanShowProperties);
    }

    public static bool ShowProperties(IEnumerable<ExplorerEntry> entries, IntPtr parentWindow)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var selection = entries.ToList();
        if (!CanShowProperties(selection)) return false;
        if (selection.Count == 1) return ShowProperties(selection[0], parentWindow);

        var pidls = new List<IntPtr>(selection.Count);
        IntPtr dataObject = IntPtr.Zero;
        try
        {
            dataObject = CreateShellSelectionDataObject(selection, pidls);
            var result = SHMultiFileProperties(dataObject, 0);
            if (result < 0)
            {
                Trace.TraceWarning($"Windows could not open the merged Properties sheet (HRESULT 0x{result:X8}).");
                return false;
            }
            return true;
        }
        finally
        {
            if (dataObject != IntPtr.Zero) Marshal.Release(dataObject);
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

    internal static bool CanCreateShellSelectionDataObject(IEnumerable<ExplorerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var selection = entries.ToList();
        if (selection.Count < 2 || !CanShowProperties(selection)) return false;

        var pidls = new List<IntPtr>(selection.Count);
        IntPtr dataObject = IntPtr.Zero;
        try
        {
            dataObject = CreateShellSelectionDataObject(selection, pidls);
            return dataObject != IntPtr.Zero;
        }
        finally
        {
            if (dataObject != IntPtr.Zero) Marshal.Release(dataObject);
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

    private static IntPtr CreateShellSelectionDataObject(IReadOnlyList<ExplorerEntry> entries, List<IntPtr> pidls)
    {
        foreach (var entry in entries)
        {
            var result = SHParseDisplayName(Path.GetFullPath(entry.FullPath), IntPtr.Zero, out var pidl, 0, IntPtr.Zero);
            if (pidl != IntPtr.Zero) pidls.Add(pidl);
            Marshal.ThrowExceptionForHR(result);
            if (pidl == IntPtr.Zero) throw new InvalidOperationException($"Windows did not return a shell item for {entry.DisplayName}.");
        }

        // Absolute item IDs are relative to the desktop shell folder, which is the common parent for search results too.
        var interfaceId = IDataObjectId;
        var createResult = SHCreateDataObject(IntPtr.Zero, (uint)pidls.Count, pidls.ToArray(), IntPtr.Zero, ref interfaceId, out var dataObject);
        if (createResult < 0)
        {
            if (dataObject != IntPtr.Zero) Marshal.Release(dataObject);
            Marshal.ThrowExceptionForHR(createResult);
        }
        if (dataObject == IntPtr.Zero) throw new InvalidOperationException("Windows did not create a shell data object for the selected items.");
        return dataObject;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string objectName, string? propertyPage);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHParseDisplayName(string displayName, IntPtr bindContext, out IntPtr itemIdList, uint attributesIn, IntPtr attributesOut);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHCreateDataObject(IntPtr parentFolderIdList, uint itemCount,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] itemIdLists,
        IntPtr innerDataObject, ref Guid interfaceId, out IntPtr dataObject);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHMultiFileProperties(IntPtr dataObject, uint flags);
}
