using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTuner;

public sealed record RunningWindow(nint Handle, string Title, string ApplicationName, string ExecutablePath, bool IsMinimized)
{
    public bool CanRunElevated => TaskbarPinCatalog.CanRunAsAdministrator(ApplicationName, ExecutablePath);
    public bool CanEndTask => ProcessId > 0;
    public int ProcessId { get; init; }
    public bool IsMaximized { get; init; }
    public bool IsForeground { get; init; }
    public bool? IsOnCurrentVirtualDesktop { get; init; }
    public TaskbarBounds Bounds { get; init; } = new(0, 0, 0, 0);
    public string? DisplayDeviceName { get; init; }
    public string? ApplicationUserModelId { get; init; }
}

public sealed class RunningWindowService
{
    private const int GW_OWNER = 4;
    private const int SW_RESTORE = 9;
    private const int SW_MAXIMIZE = 3;
    private const int SW_MINIMIZE = 6;
    private const uint WM_CLOSE = 0x0010;
    private static readonly SharedSnapshotCache<RunningWindow> SnapshotCache = new(TimeSpan.FromMilliseconds(200));
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public IReadOnlyList<RunningWindow> Enumerate() => SnapshotCache.GetOrRefresh(EnumerateWindows);

    private IReadOnlyList<RunningWindow> EnumerateWindows()
    {
        var windows = new List<RunningWindow>();
        using var virtualDesktop = VirtualDesktopWindowService.TryCreate();
        var shell = GetShellWindow();
        var foregroundWindow = GetForegroundWindow();
        EnumWindows((handle, _) =>
        {
            if (handle == shell || !IsWindowVisible(handle) || GetWindow(handle, GW_OWNER) != 0) return true;
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || processId == _ownProcessId) return true;

            var length = GetWindowTextLength(handle);
            if (length == 0) return true;
            var title = new StringBuilder(length + 1);
            GetWindowText(handle, title, title.Capacity);
            var text = title.ToString().Trim();
            if (text.Length == 0) return true;

            var appName = "Application";
            var executablePath = string.Empty;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                executablePath = process.MainModule?.FileName ?? string.Empty;
                appName = executablePath.Length > 0 ? Path.GetFileNameWithoutExtension(executablePath) : process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            var windowBounds = default(NativeRect);
            if (!GetWindowRect(handle, out windowBounds))
            {
                Trace.TraceWarning($"Could not read bounds for taskbar window '{text}' (0x{handle:X}); maximized-window detection will ignore its position.");
                windowBounds = default;
            }
            var runningWindow = new RunningWindow(handle, text, appName, executablePath, IsIconic(handle))
            {
                ProcessId = checked((int)processId),
                IsMaximized = IsZoomed(handle),
                IsForeground = handle == foregroundWindow,
                IsOnCurrentVirtualDesktop = virtualDesktop?.IsWindowOnCurrentDesktop(handle),
                Bounds = new TaskbarBounds(windowBounds.Left, windowBounds.Top, Math.Max(0, windowBounds.Right - windowBounds.Left), Math.Max(0, windowBounds.Bottom - windowBounds.Top)),
                DisplayDeviceName = TaskbarDisplayService.GetDeviceNameForWindow(handle)
            };
            windows.Add(runningWindow with { ApplicationUserModelId = TaskbarJumpListService.GetAppUserModelId(runningWindow) });
            return true;
        }, IntPtr.Zero);

        return windows.OrderBy(window => window.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void Activate(RunningWindow window)
    {
        if (IsIconic(window.Handle)) ShowWindow(window.Handle, SW_RESTORE);
        SetForegroundWindow(window.Handle);
    }

    public static void ActivateOrMinimize(RunningWindow window)
    {
        var isMinimized = IsIconic(window.Handle);
        if (TaskbarWindowActivationPolicy.ShouldMinimize(window.Handle == GetForegroundWindow(), isMinimized))
            ShowWindow(window.Handle, SW_MINIMIZE);
        else
            Activate(window);
    }

    public static void Minimize(RunningWindow window) => ShowWindow(window.Handle, SW_MINIMIZE);

    public static void Restore(RunningWindow window) => ShowWindow(window.Handle, SW_RESTORE);

    public static void Maximize(RunningWindow window) => ShowWindow(window.Handle, SW_MAXIMIZE);

    public static void Close(RunningWindow window)
    {
        if (!PostMessage(window.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            Trace.TraceWarning($"Could not request closing window '{window.Title}' (0x{window.Handle:X}).");
    }

    public static bool EndTask(RunningWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.ProcessId <= 0 || GetWindowThreadProcessId(window.Handle, out var currentProcessId) == 0 || currentProcessId != window.ProcessId)
            return false;
        try
        {
            using var process = Process.GetProcessById(window.ProcessId);
            if (process.HasExited) return true;
            process.Kill();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Trace.TraceWarning($"Could not end task for '{window.Title}' (PID {window.ProcessId}): {ex.Message}");
            return false;
        }
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);
}

public static class TaskbarWindowActivationPolicy
{
    public static bool ShouldMinimize(bool isForeground, bool isMinimized) => isForeground && !isMinimized;
}
