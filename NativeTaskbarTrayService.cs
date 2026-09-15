using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTuner;

public static class NativeTaskbarTrayService
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    private const string NotificationAreaClass = "TrayNotifyWnd";

    public readonly record struct TrayCandidate(TaskbarBounds Bounds, nint TaskbarWindow, nint TrayWindow);

    public static TaskbarBounds? FindTrayBounds(TaskbarDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        var candidates = new List<TrayCandidate>();
        try
        {
            candidates.AddRange(FindTrayCandidates(display));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Trace.TraceWarning($"Could not inspect the native Windows notification area: {ex.Message}");
            return null;
        }

        return SelectBestTrayCandidate(candidates, display)?.Bounds;
    }

    public static bool TryFocusTray(TaskbarDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        try
        {
            var candidate = SelectBestTrayCandidate(FindTrayCandidates(display), display);
            if (candidate is not { } selected) return false;
            var foregrounded = SetForegroundWindow(selected.TaskbarWindow);
            var focused = SetFocus(selected.TrayWindow) != 0;
            return foregrounded || focused;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Trace.TraceWarning($"Could not focus the native Windows notification area: {ex.Message}");
            return false;
        }
    }

    public static TaskbarBounds? SelectBestTrayBounds(IEnumerable<TaskbarBounds> candidates, TaskbarDisplay display)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(display);
        return candidates
            .Where(candidate => candidate.Width > 0 && candidate.Height > 0 && TaskbarDisplayService.Overlaps(candidate, display))
            .OrderByDescending(candidate => GetIntersectionArea(candidate, display))
            .ThenByDescending(candidate => candidate.Width * candidate.Height)
            .FirstOrDefault();
    }

    public static TrayCandidate? SelectBestTrayCandidate(IEnumerable<TrayCandidate> candidates, TaskbarDisplay display)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(display);
        var selectedBounds = SelectBestTrayBounds(candidates.Select(candidate => candidate.Bounds), display);
        if (selectedBounds is null) return null;
        return candidates.FirstOrDefault(candidate => candidate.Bounds == selectedBounds);
    }

    private static IReadOnlyList<TrayCandidate> FindTrayCandidates(TaskbarDisplay display)
    {
        var candidates = new List<TrayCandidate>();
        EnumWindows((taskbar, _) =>
        {
            var className = GetClassName(taskbar);
            if (className is not (PrimaryTaskbarClass or SecondaryTaskbarClass)) return true;

            EnumChildWindows(taskbar, (child, _) =>
            {
                if (GetClassName(child) != NotificationAreaClass || !IsWindowVisible(child)) return true;
                if (!GetWindowRect(child, out var rect)) return true;
                var bounds = new TaskbarBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                if (TaskbarDisplayService.Overlaps(bounds, display))
                    candidates.Add(new TrayCandidate(bounds, taskbar, child));
                return true;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return candidates;
    }

    private static long GetIntersectionArea(TaskbarBounds bounds, TaskbarDisplay display)
    {
        var left = Math.Max(bounds.Left, display.Left);
        var top = Math.Max(bounds.Top, display.Top);
        var right = Math.Min(bounds.Left + bounds.Width, display.Left + display.Width);
        var bottom = Math.Min(bounds.Top + bounds.Height, display.Top + display.Height);
        return right <= left || bottom <= top ? 0 : (long)((right - left) * (bottom - top));
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);
}
