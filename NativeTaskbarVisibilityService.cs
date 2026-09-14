using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;

namespace DesktopTuner;

public sealed class NativeTaskbarVisibilityService
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private readonly Dictionary<IntPtr, bool> _originalVisibility = [];

    public NativeTaskbarVisibilityService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner");
        SnapshotPath = Path.Combine(directory, $"taskbar-restore-{Environment.ProcessId}.json");
    }

    public string SnapshotPath { get; }

    public bool HideForDisplays(IEnumerable<TaskbarDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var targets = displays.ToArray();
        if (targets.Length == 0) return false;

        var taskbars = new List<IntPtr>();
        if (!EnumWindows((window, _) =>
        {
            if (!IsTaskbar(window) || !GetWindowRect(window, out var rect)) return true;
            var bounds = new TaskbarBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            if (!targets.Any(display => TaskbarDisplayService.Overlaps(bounds, display))) return true;
            taskbars.Add(window);
            return true;
        }, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate the Windows taskbars.");

        foreach (var window in taskbars)
        {
            if (!_originalVisibility.ContainsKey(window))
            {
                _originalVisibility[window] = IsWindowVisible(window);
                PersistSnapshot();
            }
            if (IsWindowVisible(window)) ShowWindow(window, SwHide);
        }

        return taskbars.Count > 0 && taskbars.All(window => !IsWindowVisible(window));
    }

    public void Restore()
    {
        RestoreWindows(_originalVisibility.Select(pair => new WindowSnapshot(pair.Key.ToInt64(), pair.Value)));
        _originalVisibility.Clear();
        try { File.Delete(SnapshotPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"Could not remove the taskbar recovery snapshot: {ex}");
        }
    }

    public static void RestoreSnapshot(string snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath)) return;
        try
        {
            var snapshot = JsonSerializer.Deserialize<List<WindowSnapshot>>(File.ReadAllText(snapshotPath));
            if (snapshot is not null) RestoreWindows(snapshot);
            File.Delete(snapshotPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceError($"Could not read the taskbar recovery snapshot: {ex}");
        }
    }

    public static int RestoreOrphanedSnapshots(string? directoryPath = null)
    {
        directoryPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner");
        if (!Directory.Exists(directoryPath)) return 0;

        string[] snapshotPaths;
        try { snapshotPaths = Directory.GetFiles(directoryPath, "taskbar-restore-*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"Could not enumerate taskbar recovery snapshots: {ex}");
            return 0;
        }

        var restoredCount = 0;
        foreach (var snapshotPath in snapshotPaths)
        {
            var fileName = Path.GetFileNameWithoutExtension(snapshotPath);
            const string prefix = "taskbar-restore-";
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(fileName.AsSpan(prefix.Length), out var ownerProcessId)
                || ownerProcessId <= 0)
                continue;

            if (IsSnapshotOwnerRunning(ownerProcessId, snapshotPath)) continue;

            RestoreSnapshot(snapshotPath);
            if (!File.Exists(snapshotPath)) restoredCount++;
        }
        return restoredCount;
    }

    private static bool IsSnapshotOwnerRunning(int ownerProcessId, string snapshotPath)
    {
        try
        {
            using var owner = Process.GetProcessById(ownerProcessId);
            return TaskbarSnapshotOwnerPolicy.IsSnapshotOwner(owner.StartTime.ToUniversalTime(), File.GetLastWriteTimeUtc(snapshotPath));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not confirm whether process {ownerProcessId} still owns taskbar recovery snapshot '{snapshotPath}': {ex.Message}");
            return true;
        }
    }

    private void PersistSnapshot()
    {
        var directory = Path.GetDirectoryName(SnapshotPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{SnapshotPath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_originalVisibility
            .Select(pair => new WindowSnapshot(pair.Key.ToInt64(), pair.Value)).ToArray()));
        File.Move(temporaryPath, SnapshotPath, overwrite: true);
    }

    private static void RestoreWindows(IEnumerable<WindowSnapshot> snapshot)
    {
        foreach (var entry in snapshot)
        {
            var window = new IntPtr(entry.Handle);
            if (!IsWindow(window) || !IsTaskbar(window)) continue;
            ShowWindow(window, entry.WasVisible ? SwShowNoActivate : SwHide);
        }
    }

    private sealed record WindowSnapshot(long Handle, bool WasVisible);

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
