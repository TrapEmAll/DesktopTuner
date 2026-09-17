using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;

namespace DesktopTuner;

public sealed class ExplorerTrayCompanionService : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private Process? _ownedProcess;
    private string? _markerPath;
    private int _recoveryAttempts;

    public bool TryRecover(out string? error)
    {
        try { return TryRecover(TaskbarDisplayService.Enumerate(), out error); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = $"Windows could not enumerate displays for the Explorer tray companion: {ex.Message}";
            return false;
        }
    }

    public bool TryRecover(IReadOnlyList<TaskbarDisplay> displays, out string? error)
    {
        error = null;
        ArgumentNullException.ThrowIfNull(displays);
        if (HasVisibleTaskbars(displays)) return true;

        var process = _ownedProcess;
        if (process is null || !process.HasExited) return false;
        if (!ExplorerTrayCompanionRecoveryPolicy.ShouldRestart(false, true, _recoveryAttempts))
        {
            error = "The Explorer tray companion exited after its bounded recovery attempt.";
            return false;
        }

        _recoveryAttempts++;
        _ownedProcess = null;
        try { process.Dispose(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Trace.TraceWarning($"Could not release the exited Explorer tray companion: {ex.Message}"); }
        if (_markerPath is { } markerPath) TryDeleteMarker(markerPath);
        _markerPath = null;
        return TryStart(displays, out error);
    }

    public static int RestoreOrphanedCompanions(string? directoryPath = null)
    {
        directoryPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner");
        if (!Directory.Exists(directoryPath)) return 0;

        var restored = 0;
        string[] paths;
        try { paths = Directory.GetFiles(directoryPath, "explorer-tray-companion-*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not enumerate Explorer tray companion markers: {ex.Message}");
            return 0;
        }

        foreach (var path in paths)
        {
            try
            {
                var marker = JsonSerializer.Deserialize<CompanionMarker>(File.ReadAllText(path));
                if (marker is null || IsProcessRunning(marker.OwnerProcessId, marker.OwnerStartUtc)) continue;
                using var explorer = Process.GetProcessById(marker.ExplorerProcessId);
                if (!SameStartTime(explorer, marker.ExplorerStartUtc))
                {
                    TryDeleteMarker(path);
                    continue;
                }
                if (!explorer.HasExited) explorer.Kill(entireProcessTree: true);
                explorer.WaitForExit(2000);
                File.Delete(path);
                restored++;
            }
            catch (ArgumentException) { TryDeleteMarker(path); }
            catch (System.ComponentModel.Win32Exception) { TryDeleteMarker(path); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
            {
                Trace.TraceWarning($"Could not recover Explorer tray companion marker '{path}': {ex.Message}");
            }
        }
        return restored;
    }

    public bool TryStart(out string? error)
    {
        try { return TryStart(TaskbarDisplayService.Enumerate(), out error); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = $"Windows could not enumerate displays for the Explorer tray companion: {ex.Message}";
            return false;
        }
    }

    public bool TryStart(IReadOnlyList<TaskbarDisplay> displays, out string? error)
    {
        error = null;
        ArgumentNullException.ThrowIfNull(displays);
        if (HasVisibleTaskbars(displays)) return true;

        try
        {
            _ownedProcess = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            if (_ownedProcess is null)
            {
                error = "Windows did not start Explorer for the tray companion.";
                return false;
            }

            _markerPath = WriteMarker(_ownedProcess);

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < StartupTimeout)
            {
                if (HasVisibleTaskbars(displays)) return true;
                if (_ownedProcess.HasExited)
                {
                    error = "Explorer exited before creating a notification-area taskbar.";
                    return false;
                }
                Thread.Sleep(PollInterval);
            }

            error = "Explorer did not create a visible taskbar within the tray-companion startup window.";
            return HasVisibleTaskbars(displays);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        var process = _ownedProcess;
        _ownedProcess = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not stop the Explorer tray companion: {ex.Message}");
        }
        finally { process.Dispose(); }
        if (_markerPath is { } markerPath) TryDeleteMarker(markerPath);
        _markerPath = null;
    }

    private static string WriteMarker(Process explorer)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"explorer-tray-companion-{Environment.ProcessId}.json");
        var marker = new CompanionMarker(Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime(), explorer.Id, explorer.StartTime.ToUniversalTime());
        File.WriteAllText(path, JsonSerializer.Serialize(marker));
        return path;
    }

    private static bool IsProcessRunning(int processId, DateTime startUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return SameStartTime(process, startUtc);
        }
        catch (ArgumentException) { return false; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Trace.TraceWarning($"Could not confirm companion owner process {processId}: {ex.Message}");
            return true;
        }
    }

    private static bool SameStartTime(Process process, DateTime expectedUtc)
    {
        try { return Math.Abs((process.StartTime.ToUniversalTime() - expectedUtc).TotalSeconds) < 2; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Trace.TraceWarning($"Could not read process start time for {process.Id}: {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteMarker(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Trace.TraceWarning($"Could not remove Explorer tray companion marker '{path}': {ex.Message}"); }
    }

    private sealed record CompanionMarker(int OwnerProcessId, DateTime OwnerStartUtc, int ExplorerProcessId, DateTime ExplorerStartUtc);

    private static bool HasVisibleTaskbars(IReadOnlyList<TaskbarDisplay> displays)
    {
        if (displays.Count == 0) return false;
        var taskbars = new List<TaskbarBounds>();
        if (!EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || !IsTaskbar(window) || !GetWindowRect(window, out var rect)) return true;
            taskbars.Add(new TaskbarBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
            return true;
        }, nint.Zero))
        {
            Trace.TraceWarning("Could not enumerate Explorer taskbars while checking tray-companion readiness.");
            return false;
        }
        return displays.All(display => taskbars.Any(bounds => TaskbarDisplayService.Overlaps(bounds, display)));
    }

    private static bool IsTaskbar(nint window)
    {
        var className = new StringBuilder(64);
        var length = GetClassName(window, className, className.Capacity);
        return length > 0 && className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

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
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
