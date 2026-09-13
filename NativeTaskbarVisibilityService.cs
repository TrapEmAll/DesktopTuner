using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTuner;

public sealed class NativeTaskbarVisibilityService
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private readonly Dictionary<IntPtr, bool> _originalVisibility = [];

    public bool HideForDisplays(IEnumerable<TaskbarDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var targets = displays.ToArray();
        if (targets.Length == 0) return false;

        var found = 0;
        var failedToHide = false;
        if (!EnumWindows((window, _) =>
        {
            if (!IsTaskbar(window) || !GetWindowRect(window, out var rect)) return true;
            var bounds = new TaskbarBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            if (!targets.Any(display => TaskbarDisplayService.Overlaps(bounds, display))) return true;

            found++;
            if (!_originalVisibility.ContainsKey(window)) _originalVisibility[window] = IsWindowVisible(window);
            if (IsWindowVisible(window)) ShowWindow(window, SwHide);
            if (IsWindowVisible(window)) failedToHide = true;
            return true;
        }, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate the Windows taskbars.");

        return found > 0 && !failedToHide;
    }

    public void Restore()
    {
        foreach (var (window, wasVisible) in _originalVisibility.ToArray())
        {
            if (!IsWindow(window) || !IsTaskbar(window)) continue;
            ShowWindow(window, wasVisible ? SwShowNoActivate : SwHide);
        }
        _originalVisibility.Clear();
    }

    private static bool IsTaskbar(IntPtr window)
    {
        var buffer = new StringBuilder(128);
        var length = GetClassNameNative(window, buffer, buffer.Capacity);
        return length > 0 && buffer.ToString() is PrimaryTaskbarClass or SecondaryTaskbarClass;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameNative(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
