using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTuner;

public enum TaskbarJumpListCategory { Recent, Frequent }

public sealed record TaskbarJumpListDestination(string Name, string ParsingName);

public static class TaskbarJumpListService
{
    private const uint DisplayNameParsing = 0x80028000;
    private const uint MaximumDestinations = 12;
    private const uint PropertyStoreDefault = 0;
    private static readonly Guid ApplicationDocumentListsClassId = new("86BEC222-30F2-47E0-9F25-60D11CD75C28");
    private static readonly Guid ApplicationDocumentListsId = new("3C594F9F-9F30-47A1-979A-C9E83D3D0A06");
    private static readonly Guid ObjectArrayId = new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid ShellLinkId = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PropertyKey AppUserModelIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public static string? GetAppUserModelId(PinnedTaskbarApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.IsDirectory || string.IsNullOrWhiteSpace(app.ExecutablePath) ||
            (!app.IsPackagedApp && !File.Exists(app.ExecutablePath))) return null;
        IPropertyStore? store = null;
        try
        {
            var parsingName = app.IsPackagedApp
                ? $"shell:AppsFolder\\{app.ExecutablePath}"
                : app.ExecutablePath;
            var interfaceId = PropertyStoreId;
            ThrowForFailure(SHGetPropertyStoreFromParsingName(parsingName, nint.Zero, PropertyStoreDefault, ref interfaceId, out store),
                "Windows could not read the pinned app's Shell properties.");
            return ReadAppUserModelId(store);
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or ArgumentException or InvalidOperationException)
        {
            Trace.TraceWarning($"Could not read the AppUserModelID for taskbar pin '{app.Name}': {ex.Message}");
            return null;
        }
        finally
        {
            if (store is not null) ReleaseComObject(store);
        }
    }

    public static string? GetAppUserModelId(RunningWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Handle == nint.Zero || !IsWindow(window.Handle)) return null;
        IPropertyStore? store = null;
        try
        {
            var interfaceId = PropertyStoreId;
            ThrowForFailure(SHGetPropertyStoreForWindow(window.Handle, ref interfaceId, out store),
                "Windows could not read the app window's Shell properties.");
            return ReadAppUserModelId(store);
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or ArgumentException or InvalidOperationException)
        {
            Trace.TraceWarning($"Could not read the AppUserModelID for window '{window.Title}': {ex.Message}");
            return null;
        }
        finally
        {
            if (store is not null) ReleaseComObject(store);
        }
    }

    public static IReadOnlyList<TaskbarJumpListDestination> GetDestinations(string? appUserModelId, TaskbarJumpListCategory category)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId) || !Enum.IsDefined(category)) return [];

        IApplicationDocumentLists? lists = null;
        IObjectArray? items = null;
        try
        {
            var listsType = Type.GetTypeFromCLSID(ApplicationDocumentListsClassId, throwOnError: true)
                ?? throw new COMException("Windows did not register its Jump List destination reader.");
            lists = (IApplicationDocumentLists?)Activator.CreateInstance(listsType)
                ?? throw new COMException("Windows could not create its Jump List destination reader.");
            ThrowForFailure(lists.SetAppID(appUserModelId), "Windows rejected the app identity for its Jump List.");

            var objectArrayId = ObjectArrayId;
            ThrowForFailure(lists.GetList((uint)category, MaximumDestinations, ref objectArrayId, out items),
                "Windows could not read this app's Jump List destinations.");
            ThrowForFailure(items.GetCount(out var count), "Windows could not count this app's Jump List destinations.");

            var destinations = new List<TaskbarJumpListDestination>((int)Math.Min(count, MaximumDestinations));
            for (uint index = 0; index < count && destinations.Count < MaximumDestinations; index++)
            {
                var destination = ReadDestination(items, index);
                if (destination is not null) destinations.Add(destination);
            }
            return TaskbarJumpListPolicy.NormalizeDestinations(destinations, (int)MaximumDestinations);
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Trace.TraceWarning($"Could not read the {category.ToString().ToLowerInvariant()} Jump List for '{appUserModelId}': {ex.Message}");
            return [];
        }
        finally
        {
            if (items is not null) ReleaseComObject(items);
            if (lists is not null) ReleaseComObject(lists);
        }
    }

    private static string? ReadAppUserModelId(IPropertyStore store)
    {
        var key = AppUserModelIdKey;
        var result = store.GetValue(ref key, out var value);
        nint stringPointer = nint.Zero;
        try
        {
            ThrowForFailure(result, "Windows could not read the app identity property.");
            if (PropVariantToStringAlloc(ref value, out stringPointer) < 0 || stringPointer == nint.Zero) return null;
            var appUserModelId = Marshal.PtrToStringUni(stringPointer)?.Trim();
            return string.IsNullOrWhiteSpace(appUserModelId) ? null : appUserModelId;
        }
        finally
        {
            if (stringPointer != nint.Zero) Marshal.FreeCoTaskMem(stringPointer);
            PropVariantClear(ref value);
        }
    }

    private static TaskbarJumpListDestination? ReadDestination(IObjectArray items, uint index)
    {
        object? shellItemObject = null;
        try
        {
            var interfaceId = ShellItemId;
            ThrowForFailure(items.GetAt(index, ref interfaceId, out shellItemObject), "Windows could not read a Jump List item.");
            var shellItem = (IShellItem)shellItemObject;
            var result = shellItem.GetDisplayName(DisplayNameParsing, out var namePointer);
            try
            {
                ThrowForFailure(result, "Windows could not resolve a Jump List item.");
                return CreateDestination(namePointer);
            }
            finally
            {
                if (result < 0 && namePointer != nint.Zero) Marshal.FreeCoTaskMem(namePointer);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            if (shellItemObject is not null && Marshal.IsComObject(shellItemObject)) ReleaseComObject(shellItemObject);
            shellItemObject = null;
            try
            {
                var interfaceId = ShellLinkId;
                ThrowForFailure(items.GetAt(index, ref interfaceId, out shellItemObject), "Windows could not read a Jump List shortcut.");
                var link = (IShellLinkW)shellItemObject;
                var path = new StringBuilder(32768);
                ThrowForFailure(link.GetPath(path, path.Capacity, nint.Zero, 0), "Windows could not resolve a Jump List shortcut.");
                var value = path.ToString().Trim();
                return string.IsNullOrWhiteSpace(value) ? null : new TaskbarJumpListDestination(Path.GetFileName(value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), value);
            }
            catch (Exception linkException) when (linkException is COMException or InvalidCastException)
            {
                Trace.TraceWarning($"Skipping an unreadable Jump List destination: {linkException.Message}");
                return null;
            }
        }
        finally
        {
            if (shellItemObject is not null && Marshal.IsComObject(shellItemObject)) ReleaseComObject(shellItemObject);
        }
    }

    private static TaskbarJumpListDestination? CreateDestination(nint namePointer)
    {
        try
        {
            if (namePointer == nint.Zero) return null;
            var parsingName = Marshal.PtrToStringUni(namePointer)?.Trim();
            if (string.IsNullOrWhiteSpace(parsingName)) return null;
            var name = Path.GetFileName(parsingName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) name = parsingName;
            return new TaskbarJumpListDestination(name, parsingName);
        }
        finally
        {
            if (namePointer != nint.Zero) Marshal.FreeCoTaskMem(namePointer);
        }
    }

    private static void ThrowForFailure(int hresult, string message)
    {
        if (hresult < 0) throw new COMException(message, hresult);
    }

    private static void ReleaseComObject(object value)
    {
        if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort VariantType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Value;
        public nint Value2;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("3C594F9F-9F30-47A1-979A-C9E83D3D0A06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationDocumentLists
    {
        [PreserveSig] int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int GetList(uint listType, uint desiredItemCount, ref Guid interfaceId, out IObjectArray items);
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint bindContext, ref Guid handlerId, ref Guid interfaceId, out nint result);
        [PreserveSig] int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint displayNameType, out nint displayName);
        [PreserveSig] int GetAttributes(uint attributeMask, out uint attributes);
        [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int characterCount, nint findData, uint flags);
        [PreserveSig] int GetIDList(out nint itemIdList);
        [PreserveSig] int SetIDList(nint itemIdList);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int characterCount);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int characterCount);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int characterCount);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCommand);
        [PreserveSig] int SetShowCmd(int showCommand);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int characterCount, out int iconIndex);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(nint window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string parsingName, nint bindContext, uint flags, ref Guid interfaceId, out IPropertyStore propertyStore);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid interfaceId, out IPropertyStore propertyStore);

    [DllImport("propsys.dll", PreserveSig = true)]
    private static extern int PropVariantToStringAlloc(ref PropVariant value, out nint result);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
