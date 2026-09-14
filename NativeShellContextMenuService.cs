using System.Runtime.InteropServices;
using System.IO;
using System.ComponentModel;
using System.Windows.Interop;

namespace DesktopTuner;

public static class NativeShellContextMenuService
{
    private const uint IdCommandFirst = 1;
    private const uint IdCommandLast = 0x7fff;
    private const uint TrackPopupMenuRightButton = 0x0002;
    private const uint TrackPopupMenuReturnCommand = 0x0100;
    private const int MessageInitMenuPopup = 0x0117;
    private const int MessageDrawItem = 0x002b;
    private const int MessageMeasureItem = 0x002c;
    private const int MessageMenuCharacter = 0x0120;
    private const uint ShellAttributeCanRename = 0x00000010;
    private const uint DesktopAbsoluteParsing = 0x80028000;
    private const uint DragDropAllowedEffects = 0x00000007;
    private const uint MouseButtonMask = 0x00000013;
    private const int DragDropCancel = 0x00040101;
    private const int DragDropComplete = 0x00040100;
    private const int DragDropDefaultCursors = 0x00040102;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");

    public static async Task<bool> ShowForItemsAsync(nint owner, IEnumerable<string> paths)
    {
        return await ProcessItemsAsync(owner, paths, showPopup: true);
    }

    public static async Task<bool> ShowForShellItemAsync(nint owner, string parsingName)
    {
        return await ProcessShellItemAsync(owner, parsingName, showPopup: true);
    }

    public static async Task<bool> ShowForShellItemsAsync(nint owner, IEnumerable<string> parsingNames)
    {
        var selection = NativeShellContextMenuPolicy.NormalizeShellSelection(parsingNames);
        var absolutePidls = await Task.Run(() => ParseDisplayNames(selection));
        return ShowForShellItems(owner, absolutePidls);
    }

    public static async Task<bool> DragShellItemsAsync(nint owner, IEnumerable<string> parsingNames)
    {
        var selection = NativeShellContextMenuPolicy.NormalizeShellSelection(parsingNames);
        var absolutePidls = await Task.Run(() => ParseDisplayNames(selection));
        return TransferShellItems(owner, absolutePidls, startDrag: true);
    }

    internal static async Task<bool> ProbeShellItemDataObjectAsync(string parsingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
        var absolutePidl = await Task.Run(() => ParseDisplayName(parsingName));
        return TransferShellItems(nint.Zero, [absolutePidl], startDrag: false);
    }

    internal static async Task<bool> ProbeItemsContextMenuAsync(IEnumerable<string> paths)
    {
        return await ProcessItemsAsync(nint.Zero, paths, showPopup: false);
    }

    internal static async Task<bool> ProbeShellItemContextMenuAsync(string parsingName)
    {
        return await ProcessShellItemAsync(nint.Zero, parsingName, showPopup: false);
    }

    internal static bool CanRenameShellItem(string parsingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
        var initializeResult = CoInitializeEx(nint.Zero, 0);
        var uninitialize = initializeResult >= 0;
        if (initializeResult < 0 && initializeResult != unchecked((int)0x80010106))
            ThrowForFailure(initializeResult, "Could not initialize the Windows Shell rename query.");

        nint absolutePidl = nint.Zero;
        nint childArray = nint.Zero;
        IShellFolder? parent = null;
        try
        {
            ThrowForFailure(SHParseDisplayName(parsingName, nint.Zero, out absolutePidl, 0, nint.Zero), $"Windows could not resolve '{parsingName}'.");
            var folderId = ShellFolderId;
            ThrowForFailure(SHBindToParent(absolutePidl, ref folderId, out parent, out var childPidl), "Could not bind to the Shell item's parent folder.");
            childArray = AllocatePointerArray([childPidl]);
            uint attributes = ShellAttributeCanRename;
            var result = parent.GetAttributesOf(1, childArray, ref attributes);
            return result >= 0 && (attributes & ShellAttributeCanRename) != 0;
        }
        catch (COMException ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not query rename support for Shell item '{parsingName}': {ex.Message}");
            return false;
        }
        finally
        {
            if (parent is not null) ReleaseComObject(parent);
            if (childArray != nint.Zero) Marshal.FreeHGlobal(childArray);
            if (absolutePidl != nint.Zero) Marshal.FreeCoTaskMem(absolutePidl);
            if (uninitialize) CoUninitialize();
        }
    }

    internal static Task<string> RenameShellItemAsync(string parsingName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        return Task.Run(() => RenameShellItem(parsingName, newName));
    }

    private static string RenameShellItem(string parsingName, string newName)
    {
        var initializeResult = CoInitializeEx(nint.Zero, 0);
        var uninitialize = initializeResult >= 0;
        if (initializeResult < 0 && initializeResult != unchecked((int)0x80010106))
            ThrowForFailure(initializeResult, "Could not initialize the Windows Shell rename thread.");

        nint absolutePidl = nint.Zero;
        nint childArray = nint.Zero;
        nint renamedChildPidl = nint.Zero;
        nint renamedAbsolutePidl = nint.Zero;
        IShellFolder? parent = null;
        try
        {
            ThrowForFailure(SHParseDisplayName(parsingName, nint.Zero, out absolutePidl, 0, nint.Zero), $"Windows could not resolve '{parsingName}'.");
            var folderId = ShellFolderId;
            ThrowForFailure(SHBindToParent(absolutePidl, ref folderId, out parent, out var childPidl), "Could not bind to the Shell item's parent folder.");
            childArray = AllocatePointerArray([childPidl]);
            uint attributes = ShellAttributeCanRename;
            ThrowForFailure(parent.GetAttributesOf(1, childArray, ref attributes), "Windows could not check whether this Shell item can be renamed.");
            if ((attributes & ShellAttributeCanRename) == 0)
                throw new InvalidOperationException("Windows does not allow this Shell item to be renamed.");

            ThrowForFailure(parent.SetNameOf(nint.Zero, childPidl, newName, 0, out renamedChildPidl), "Windows could not rename this Shell item.");
            if (renamedChildPidl == nint.Zero) throw new COMException("Windows renamed the Shell item but did not return its new identity.");
            renamedAbsolutePidl = CombinePidls(absolutePidl, renamedChildPidl);
            ThrowForFailure(SHGetNameFromIDList(renamedAbsolutePidl, DesktopAbsoluteParsing, out var parsingNamePointer), "Windows could not resolve the renamed Shell item's identity.");
            try
            {
                return Marshal.PtrToStringUni(parsingNamePointer) ?? throw new COMException("Windows returned an empty parsing name for the renamed Shell item.");
            }
            finally { Marshal.FreeCoTaskMem(parsingNamePointer); }
        }
        finally
        {
            if (parent is not null) ReleaseComObject(parent);
            if (childArray != nint.Zero) Marshal.FreeHGlobal(childArray);
            if (renamedChildPidl != nint.Zero) Marshal.FreeCoTaskMem(renamedChildPidl);
            if (renamedAbsolutePidl != nint.Zero) Marshal.FreeCoTaskMem(renamedAbsolutePidl);
            if (absolutePidl != nint.Zero) Marshal.FreeCoTaskMem(absolutePidl);
            if (uninitialize) CoUninitialize();
        }
    }

    private static nint CombinePidls(nint absolutePidl, nint childPidl)
    {
        var parentPidlLength = GetParentPidlLength(absolutePidl);
        var childPidlLength = GetPidlLength(childPidl);
        var parentComponentsLength = parentPidlLength - sizeof(ushort);
        var combined = Marshal.AllocCoTaskMem(checked(parentComponentsLength + childPidlLength));
        try
        {
            for (var index = 0; index < parentComponentsLength; index++)
                Marshal.WriteByte(combined, index, Marshal.ReadByte(absolutePidl, index));
            for (var index = 0; index < childPidlLength; index++)
                Marshal.WriteByte(combined, parentComponentsLength + index, Marshal.ReadByte(childPidl, index));
            return combined;
        }
        catch
        {
            Marshal.FreeCoTaskMem(combined);
            throw;
        }
    }

    internal static async Task<bool> ProbeShellItemsContextMenuAsync(IEnumerable<string> parsingNames)
    {
        var selection = NativeShellContextMenuPolicy.NormalizeShellSelection(parsingNames);
        var absolutePidls = await Task.Run(() => ParseDisplayNames(selection));
        return ShowForShellItems(nint.Zero, absolutePidls, showPopup: false);
    }

    private static async Task<bool> ProcessItemsAsync(nint owner, IEnumerable<string> paths, bool showPopup)
    {
        var selection = NativeShellContextMenuPolicy.NormalizeSelection(paths);
        var absolutePidl = await Task.Run(() => ParseDisplayName(selection[0]));
        return ShowForItems(owner, selection, absolutePidl, showPopup);
    }

    private static async Task<bool> ProcessShellItemAsync(nint owner, string parsingName, bool showPopup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
        var absolutePidl = await Task.Run(() => ParseDisplayName(parsingName));
        return ShowForShellItem(owner, absolutePidl, showPopup);
    }

    internal static async Task<bool> ProbeFolderBackgroundContextMenuAsync(string folderPath)
    {
        return await ProcessFolderBackgroundAsync(nint.Zero, folderPath, showPopup: false);
    }

    private static async Task<bool> ProcessFolderBackgroundAsync(nint owner, string folderPath, bool showPopup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var location = DesktopShellNamespaceCatalog.IsShellNamespaceLocation(folderPath)
            ? folderPath.Trim()
            : Path.GetFullPath(folderPath);
        if (!DesktopShellNamespaceCatalog.IsShellNamespaceLocation(location) && !Directory.Exists(location))
            throw new DirectoryNotFoundException($"The folder no longer exists: {location}");
        var absolutePidl = await Task.Run(() => ParseDisplayName(location));
        return ShowFolderBackground(owner, absolutePidl, showPopup);
    }

    private static bool ShowForItems(nint owner, IReadOnlyList<string> selection, nint absolutePidl, bool showPopup)
    {
        IShellFolder? parent = null;
        IContextMenu? contextMenu = null;
        var childPidls = new List<nint>();
        nint childArray = nint.Zero;
        try
        {
            var folderId = ShellFolderId;
            ThrowForFailure(SHBindToParent(absolutePidl, ref folderId, out parent, out _), "Could not bind to the selected items' parent folder.");
            foreach (var path in selection)
            {
                uint eaten = 0;
                uint attributes = 0;
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var parseResult = parent.ParseDisplayName(owner, nint.Zero, name, out eaten, out var childPidl, ref attributes);
                if (parseResult < 0)
                {
                    if (childPidl != nint.Zero) Marshal.FreeCoTaskMem(childPidl);
                    ThrowForFailure(parseResult, $"Could not resolve '{name}' in its parent folder.");
                }
                childPidls.Add(childPidl);
            }

            childArray = AllocatePointerArray(childPidls);
            var contextMenuId = ContextMenuId;
            ThrowForFailure(parent.GetUIObjectOf(owner, (uint)childPidls.Count, childArray, ref contextMenuId, nint.Zero, out contextMenu), "Windows could not create a context menu for the selection.");
            return showPopup ? ShowMenu(owner, contextMenu) : PopulateMenu(contextMenu, "Windows could not populate the context menu.");
        }
        finally
        {
            if (contextMenu is not null) ReleaseComObject(contextMenu);
            if (parent is not null) ReleaseComObject(parent);
            foreach (var childPidl in childPidls) Marshal.FreeCoTaskMem(childPidl);
            if (childArray != nint.Zero) Marshal.FreeHGlobal(childArray);
            Marshal.FreeCoTaskMem(absolutePidl);
        }
    }

    private static bool ShowForShellItem(nint owner, nint absolutePidl, bool showPopup)
    {
        IShellFolder? parent = null;
        IContextMenu? contextMenu = null;
        nint childArray = nint.Zero;
        try
        {
            var folderId = ShellFolderId;
            ThrowForFailure(SHBindToParent(absolutePidl, ref folderId, out parent, out var childPidl), "Could not bind to the Shell item's parent folder.");
            childArray = AllocatePointerArray([childPidl]);
            var contextMenuId = ContextMenuId;
            ThrowForFailure(parent.GetUIObjectOf(owner, 1, childArray, ref contextMenuId, nint.Zero, out contextMenu), "Windows could not create a context menu for the Shell item.");
            return showPopup ? ShowMenu(owner, contextMenu) : PopulateMenu(contextMenu, "Windows could not populate the Shell item's context menu.");
        }
        finally
        {
            if (contextMenu is not null) ReleaseComObject(contextMenu);
            if (parent is not null) ReleaseComObject(parent);
            if (childArray != nint.Zero) Marshal.FreeHGlobal(childArray);
            Marshal.FreeCoTaskMem(absolutePidl);
        }
    }

    private static bool ShowForShellItems(nint owner, nint[] absolutePidls, bool showPopup = true)
    {
        if (absolutePidls.Length == 0) throw new ArgumentException("Select at least one Windows Shell item.", nameof(absolutePidls));
        IShellFolder? parent = null;
        IContextMenu? contextMenu = null;
        nint childArray = nint.Zero;
        try
        {
            var parentLength = GetParentPidlLength(absolutePidls[0]);
            if (absolutePidls.Skip(1).Any(pidl => !ParentPidlsEqual(absolutePidls[0], parentLength, pidl)))
                throw new ArgumentException("Windows can show a shared context menu only for items in the same Shell folder.", nameof(absolutePidls));

            var folderId = ShellFolderId;
            ThrowForFailure(SHBindToParent(absolutePidls[0], ref folderId, out parent, out _), "Could not bind to the selected items' parent folder.");
            var childPidls = absolutePidls.Select(pidl => pidl + GetParentPidlLength(pidl) - sizeof(ushort)).ToArray();
            if (childPidls.Any(child => child == nint.Zero)) throw new COMException("Could not resolve the selected Shell items.");
            childArray = AllocatePointerArray(childPidls);
            var contextMenuId = ContextMenuId;
            ThrowForFailure(parent.GetUIObjectOf(owner, (uint)childPidls.Length, childArray, ref contextMenuId, nint.Zero, out contextMenu), "Windows could not create a context menu for the selection.");
            return showPopup ? ShowMenu(owner, contextMenu) : PopulateMenu(contextMenu, "Windows could not populate the context menu for the selection.");
        }
        finally
        {
            if (contextMenu is not null) ReleaseComObject(contextMenu);
            if (parent is not null) ReleaseComObject(parent);
            if (childArray != nint.Zero) Marshal.FreeHGlobal(childArray);
            foreach (var absolutePidl in absolutePidls) Marshal.FreeCoTaskMem(absolutePidl);
        }
    }

    private static bool TransferShellItems(nint owner, nint[] absolutePidls, bool startDrag)
    {
        if (absolutePidls.Length == 0) throw new ArgumentException("Select at least one Windows Shell item.", nameof(absolutePidls));
        var initializeResult = CoInitializeEx(nint.Zero, 0);
        var uninitialize = initializeResult >= 0;
        if (initializeResult < 0 && initializeResult != unchecked((int)0x80010106))
            ThrowForFailure(initializeResult, "Could not initialize the Windows Shell drag operation.");

        IShellFolder? parent = null;
        System.Runtime.InteropServices.ComTypes.IDataObject? dataObject = null;
        nint childArray = nint.Zero;
        try
        {
            var parentLength = GetParentPidlLength(absolutePidls[0]);
            if (absolutePidls.Skip(1).Any(pidl => !ParentPidlsEqual(absolutePidls[0], parentLength, pidl)))
                throw new ArgumentException("Windows can drag a combined Shell selection only when its items share a Shell folder.", nameof(absolutePidls));

            var folderId = ShellFolderId;
            ThrowForFailure(SHBindToParent(absolutePidls[0], ref folderId, out parent, out _), "Could not bind to the selected items' parent folder.");
            var childPidls = absolutePidls.Select(pidl => pidl + GetParentPidlLength(pidl) - sizeof(ushort)).ToArray();
            if (childPidls.Any(child => child == nint.Zero)) throw new COMException("Could not resolve the selected Shell items.");
            childArray = AllocatePointerArray(childPidls);
            var dataObjectId = DataObjectId;
            var shellFolder = (IShellFolderDataObject)parent;
            ThrowForFailure(shellFolder.GetUIObjectOf(owner, (uint)childPidls.Length, childArray, ref dataObjectId, nint.Zero, out dataObject),
                "Windows could not create a native drag object for the Shell selection.");

            if (!startDrag) return true;
            ThrowForFailure(DoDragDrop(dataObject, new NativeShellDropSource(), DragDropAllowedEffects, out var effect), "Windows could not start the Shell drag operation.");
            return effect != 0;
        }
        finally
        {
            if (dataObject is not null && Marshal.IsComObject(dataObject)) Marshal.ReleaseComObject(dataObject);
            if (parent is not null) ReleaseComObject(parent);
            if (childArray != nint.Zero) Marshal.FreeHGlobal(childArray);
            foreach (var absolutePidl in absolutePidls) Marshal.FreeCoTaskMem(absolutePidl);
            if (uninitialize) CoUninitialize();
        }
    }

    public static async Task<bool> ShowForFolderBackgroundAsync(nint owner, string folderPath)
    {
        return await ProcessFolderBackgroundAsync(owner, folderPath, showPopup: true);
    }

    public static async Task<bool> ShowForShellFolderBackgroundAsync(nint owner, string parsingName)
    {
        return await ProcessFolderBackgroundAsync(owner, parsingName, showPopup: true);
    }

    public static Task<bool> ShowForDesktopBackgroundAsync(nint owner) => Task.FromResult(ShowDesktopBackground(owner, showPopup: true));

    internal static Task<bool> ProbeDesktopBackgroundContextMenuAsync() => Task.FromResult(ShowDesktopBackground(nint.Zero, showPopup: false));

    private static bool ShowDesktopBackground(nint owner, bool showPopup)
    {
        var initializeResult = CoInitializeEx(nint.Zero, 0);
        var uninitialize = initializeResult >= 0;
        if (initializeResult < 0 && initializeResult != unchecked((int)0x80010106))
            ThrowForFailure(initializeResult, "Could not initialize the Windows desktop context menu.");

        IShellFolder? desktop = null;
        IContextMenu? contextMenu = null;
        try
        {
            ThrowForFailure(SHGetDesktopFolder(out desktop), "Windows could not open the desktop Shell folder.");
            var contextMenuId = ContextMenuId;
            ThrowForFailure(desktop.CreateViewObject(owner, ref contextMenuId, out contextMenu), "Windows could not create the desktop background context menu.");
            return showPopup ? ShowMenu(owner, contextMenu) : PopulateMenu(contextMenu, "Windows could not populate the desktop background context menu.");
        }
        finally
        {
            if (contextMenu is not null) ReleaseComObject(contextMenu);
            if (desktop is not null) ReleaseComObject(desktop);
            if (uninitialize) CoUninitialize();
        }
    }

    private static bool ShowFolderBackground(nint owner, nint absolutePidl, bool showPopup)
    {
        IShellFolder? parent = null;
        IShellFolder? folder = null;
        IContextMenu? contextMenu = null;
        try
        {
            var folderId = ShellFolderId;
            ThrowForFailure(SHBindToParent(absolutePidl, ref folderId, out parent, out var childPidl), "Could not bind to the folder's parent.");
            ThrowForFailure(parent.BindToObject(childPidl, nint.Zero, ref folderId, out folder), "Could not open the folder's Shell view.");
            var contextMenuId = ContextMenuId;
            ThrowForFailure(folder.CreateViewObject(owner, ref contextMenuId, out contextMenu), "Windows could not create a folder background context menu.");
            return showPopup ? ShowMenu(owner, contextMenu) : PopulateMenu(contextMenu, "Windows could not populate the folder background context menu.");
        }
        finally
        {
            if (contextMenu is not null) ReleaseComObject(contextMenu);
            if (folder is not null) ReleaseComObject(folder);
            if (parent is not null) ReleaseComObject(parent);
            Marshal.FreeCoTaskMem(absolutePidl);
        }
    }

    private static bool ShowMenu(nint owner, IContextMenu contextMenu)
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero) throw new COMException("Windows could not create a context menu.", Marshal.GetLastWin32Error());
        var source = HwndSource.FromHwnd(owner);
        IContextMenu2? contextMenu2 = contextMenu as IContextMenu2;
        IContextMenu3? contextMenu3 = contextMenu as IContextMenu3;
        HwndSourceHook hook = (nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
        {
            if (message == MessageMenuCharacter && contextMenu3 is not null)
            {
                var menuMessageResult = contextMenu3.HandleMenuMsg2((uint)message, wParam, lParam, out var menuResult);
                if (menuMessageResult >= 0) handled = true;
                return menuResult;
            }
            if (contextMenu2 is null || message is not (MessageInitMenuPopup or MessageDrawItem or MessageMeasureItem)) return nint.Zero;
            var result = contextMenu2.HandleMenuMsg((uint)message, wParam, lParam);
            if (result >= 0) handled = true;
            return nint.Zero;
        };
        try
        {
            ThrowForFailure(contextMenu.QueryContextMenu(menu, 0, IdCommandFirst, IdCommandLast, 0), "Windows could not populate the context menu.");
            source?.AddHook(hook);
            SetForegroundWindow(owner);
            if (!GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not locate the pointer for the Shell context menu.");
            var command = TrackPopupMenuEx(menu, TrackPopupMenuRightButton | TrackPopupMenuReturnCommand, point.X, point.Y, owner, nint.Zero);
            PostMessage(owner, 0, nint.Zero, nint.Zero);
            if (command == 0) return false;

            var invoke = new CommandInfo
            {
                Size = (uint)Marshal.SizeOf<CommandInfo>(),
                Window = owner,
                Verb = new nint((int)(command - IdCommandFirst)),
                ShowCommand = 1
            };
            ThrowForFailure(contextMenu.InvokeCommand(ref invoke), "Windows could not run the selected context-menu command.");
            return true;
        }
        finally
        {
            source?.RemoveHook(hook);
            DestroyMenu(menu);
        }
    }

    private static bool PopulateMenu(IContextMenu contextMenu, string failureMessage)
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero) throw new COMException("Windows could not create a context menu.", Marshal.GetLastWin32Error());
        try
        {
            ThrowForFailure(contextMenu.QueryContextMenu(menu, 0, IdCommandFirst, IdCommandLast, 0), failureMessage);
            return true;
        }
        finally { DestroyMenu(menu); }
    }

    private static nint ParseDisplayName(string path)
    {
        var initializeResult = CoInitializeEx(nint.Zero, 0);
        var uninitialize = initializeResult >= 0;
        if (initializeResult < 0 && initializeResult != unchecked((int)0x80010106))
            ThrowForFailure(initializeResult, "Could not initialize the Windows Shell parsing thread.");
        try
        {
            ThrowForFailure(SHParseDisplayName(path, nint.Zero, out var pidl, 0, nint.Zero), $"Windows could not resolve '{path}'.");
            return pidl;
        }
        finally
        {
            if (uninitialize) CoUninitialize();
        }
    }

    private static nint AllocatePointerArray(IReadOnlyList<nint> pointers)
    {
        var array = Marshal.AllocHGlobal(IntPtr.Size * pointers.Count);
        for (var index = 0; index < pointers.Count; index++) Marshal.WriteIntPtr(array, index * IntPtr.Size, pointers[index]);
        return array;
    }

    private static nint[] ParseDisplayNames(IReadOnlyList<string> parsingNames)
    {
        var pidls = new List<nint>(parsingNames.Count);
        try
        {
            foreach (var parsingName in parsingNames) pidls.Add(ParseDisplayName(parsingName));
            return pidls.ToArray();
        }
        catch
        {
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
            throw;
        }
    }

    private static int GetParentPidlLength(nint pidl)
    {
        var offset = 0;
        var lastItemLength = 0;
        while (true)
        {
            var itemLength = (ushort)Marshal.ReadInt16(pidl, offset);
            if (itemLength == 0) return offset + sizeof(ushort) - lastItemLength;
            if (itemLength < sizeof(ushort)) throw new COMException("Windows returned an invalid Shell item identifier.");
            lastItemLength = itemLength;
            offset = checked(offset + itemLength);
            if (offset > 65536) throw new COMException("Windows returned an oversized Shell item identifier.");
        }
    }

    private static int GetPidlLength(nint pidl)
    {
        var offset = 0;
        while (true)
        {
            var itemLength = (ushort)Marshal.ReadInt16(pidl, offset);
            if (itemLength == 0) return offset + sizeof(ushort);
            if (itemLength < sizeof(ushort)) throw new COMException("Windows returned an invalid Shell item identifier.");
            offset = checked(offset + itemLength);
            if (offset > 65536) throw new COMException("Windows returned an oversized Shell item identifier.");
        }
    }

    private static bool ParentPidlsEqual(nint first, int firstParentLength, nint second)
    {
        var secondParentLength = GetParentPidlLength(second);
        if (firstParentLength != secondParentLength) return false;
        // The absolute PIDL continues with a child item where a standalone parent PIDL
        // would contain its two-byte terminator, so compare only the parent components.
        for (var index = 0; index < firstParentLength - sizeof(ushort); index++)
            if (Marshal.ReadByte(first, index) != Marshal.ReadByte(second, index)) return false;
        return true;
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
    private struct CommandInfo
    {
        public uint Size;
        public uint Mask;
        public nint Window;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int ShowCommand;
        public uint HotKey;
        public nint Icon;
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(nint hwnd, nint bindContext, [MarshalAs(UnmanagedType.LPWStr)] string displayName, out uint eaten, out nint pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(nint hwnd, uint flags, out nint enumerator);
        [PreserveSig] int BindToObject(nint pidl, nint bindContext, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder);
        [PreserveSig] int BindToStorage(nint pidl, nint bindContext, ref Guid interfaceId, out nint storage);
        [PreserveSig] int CompareIDs(nint parameter, nint first, nint second);
        [PreserveSig] int CreateViewObject(nint hwnd, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IContextMenu contextMenu);
        [PreserveSig] int GetAttributesOf(uint count, nint pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(nint hwnd, uint count, nint pidls, ref Guid interfaceId, nint reserved, [MarshalAs(UnmanagedType.Interface)] out IContextMenu contextMenu);
        [PreserveSig] int GetDisplayNameOf(nint pidl, uint flags, out nint displayName);
        [PreserveSig] int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint newPidl);
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolderDataObject
    {
        [PreserveSig] int ParseDisplayName(nint hwnd, nint bindContext, [MarshalAs(UnmanagedType.LPWStr)] string displayName, out uint eaten, out nint pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(nint hwnd, uint flags, out nint enumerator);
        [PreserveSig] int BindToObject(nint pidl, nint bindContext, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder);
        [PreserveSig] int BindToStorage(nint pidl, nint bindContext, ref Guid interfaceId, out nint storage);
        [PreserveSig] int CompareIDs(nint parameter, nint first, nint second);
        [PreserveSig] int CreateViewObject(nint hwnd, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IContextMenu contextMenu);
        [PreserveSig] int GetAttributesOf(uint count, nint pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(nint hwnd, uint count, nint pidls, ref Guid interfaceId, nint reserved,
            [MarshalAs(UnmanagedType.Interface)] out System.Runtime.InteropServices.ComTypes.IDataObject dataObject);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class NativeShellDropSource : IDropSource
    {
        public int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escapePressed, uint keyState) =>
            escapePressed ? DragDropCancel : (keyState & MouseButtonMask) == 0 ? DragDropComplete : 0;

        public int GiveFeedback(uint effect) => DragDropDefaultCursors;
    }

    [ComVisible(true)]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropSource
    {
        [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escapePressed, uint keyState);
        [PreserveSig] int GiveFeedback(uint effect);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint indexMenu, uint idCommandFirst, uint idCommandLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInfo commandInfo);
        [PreserveSig] int GetCommandString(nuint commandId, uint type, nint reserved, nint name, uint nameCapacity);
    }

    [ComImport]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2 : IContextMenu
    {
        [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
    }

    [ComImport]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3 : IContextMenu2
    {
        [PreserveSig] int HandleMenuMsg2(uint message, nint wParam, nint lParam, out nint result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, nint attributesOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetDesktopFolder([MarshalAs(UnmanagedType.Interface)] out IShellFolder desktopFolder);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint concurrencyModel);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int DoDragDrop([MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject dataObject,
        [MarshalAs(UnmanagedType.Interface)] IDropSource dropSource, uint allowedEffects, out uint effect);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToParent(nint pidl, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellFolder parent, out nint childPidl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
