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
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");

    public static async Task<bool> ShowForItemsAsync(nint owner, IEnumerable<string> paths)
    {
        return await ProcessItemsAsync(owner, paths, showPopup: true);
    }

    internal static async Task<bool> ProbeItemsContextMenuAsync(IEnumerable<string> paths)
    {
        return await ProcessItemsAsync(nint.Zero, paths, showPopup: false);
    }

    private static async Task<bool> ProcessItemsAsync(nint owner, IEnumerable<string> paths, bool showPopup)
    {
        var selection = NativeShellContextMenuPolicy.NormalizeSelection(paths);
        var absolutePidl = await Task.Run(() => ParseDisplayName(selection[0]));
        return ShowForItems(owner, selection, absolutePidl, showPopup);
    }

    internal static async Task<bool> ProbeFolderBackgroundContextMenuAsync(string folderPath)
    {
        return await ProcessFolderBackgroundAsync(nint.Zero, folderPath, showPopup: false);
    }

    private static async Task<bool> ProcessFolderBackgroundAsync(nint owner, string folderPath, bool showPopup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var fullPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"The folder no longer exists: {fullPath}");
        var absolutePidl = await Task.Run(() => ParseDisplayName(fullPath));
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

    public static async Task<bool> ShowForFolderBackgroundAsync(nint owner, string folderPath)
    {
        return await ProcessFolderBackgroundAsync(owner, folderPath, showPopup: true);
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

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint concurrencyModel);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

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
