using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class NativeWindowSystemMenuService
{
    private const uint TrackPopupMenuRightButton = 0x0002;
    private const uint TrackPopupMenuReturnCommand = 0x0100;
    private const uint WM_NULL = 0x0000;
    private const uint WM_SYSCOMMAND = 0x0112;

    public static bool TryShow(nint targetWindow, nint ownerWindow)
    {
        if (targetWindow == 0 || ownerWindow == 0 || !IsWindow(targetWindow)) return false;
        var menu = GetSystemMenu(targetWindow, false);
        if (menu == 0 || !GetCursorPos(out var point)) return false;

        SetForegroundWindow(ownerWindow);
        var command = TrackPopupMenuEx(menu, TrackPopupMenuRightButton | TrackPopupMenuReturnCommand,
            point.X, point.Y, ownerWindow, 0);
        PostMessage(ownerWindow, WM_NULL, 0, 0);
        if (command == 0) return true;

        return PostMessage(targetWindow, WM_SYSCOMMAND, (nint)command, 0);
    }

    [DllImport("user32.dll")]
    private static extern nint GetSystemMenu(nint window, [MarshalAs(UnmanagedType.Bool)] bool revert);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
