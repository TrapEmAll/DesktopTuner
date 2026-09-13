using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTuner;

public sealed record RunningWindow(nint Handle, string Title, string ApplicationName, string ExecutablePath, bool IsMinimized);

public sealed class RunningWindowService
{
    private const int GW_OWNER = 4;
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public IReadOnlyList<RunningWindow> Enumerate()
    {
        var windows = new List<RunningWindow>();
        var shell = GetShellWindow();
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
            windows.Add(new RunningWindow(handle, text, appName, executablePath, IsIconic(handle)));
            return true;
        }, IntPtr.Zero);

        return windows.OrderBy(window => window.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void Activate(RunningWindow window)
    {
        if (window.IsMinimized) ShowWindow(window.Handle, SW_RESTORE);
        SetForegroundWindow(window.Handle);
    }

    public static void Minimize(RunningWindow window) => ShowWindow(window.Handle, SW_MINIMIZE);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

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
    private static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
