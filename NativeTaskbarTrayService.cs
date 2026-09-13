using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTuner;

public static class NativeTaskbarTrayService
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    private const string NotificationAreaClass = "TrayNotifyWnd";

    public static TaskbarBounds? FindTrayBounds(TaskbarDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        TaskbarBounds? result = null;
        try
        {
            EnumWindows((taskbar, _) =>
            {
                var className = GetClassName(taskbar);
                if (className is not (PrimaryTaskbarClass or SecondaryTaskbarClass)) return true;

                EnumChildWindows(taskbar, (child, _) =>
                {
                    if (GetClassName(child) != NotificationAreaClass || !IsWindowVisible(child)) return true;
                    if (!GetWindowRect(child, out var rect)) return true;

                    var bounds = new TaskbarBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                    if (TaskbarDisplayService.Overlaps(bounds, display)) result = bounds;
                    return true;
                }, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Trace.TraceWarning($"Could not inspect the native Windows notification area: {ex.Message}");
            return null;
        }

        return result;
    }

    private static string GetClassName(IntPtr window)
    {
        var buffer = new StringBuilder(128);
        return GetClassNameNative(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameNative(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
}
