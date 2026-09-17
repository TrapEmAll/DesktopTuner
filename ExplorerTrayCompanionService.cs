using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DesktopTuner;

public sealed class ExplorerTrayCompanionService : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private Process? _ownedProcess;
    private string? _markerPath;

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
        error = null;
        if (HasVisibleTaskbar()) return true;

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
                if (HasVisibleTaskbar()) return true;
                if (_ownedProcess.HasExited)
                {
                    error = "Explorer exited before creating a notification-area taskbar.";
                    return false;
                }
                Thread.Sleep(PollInterval);
            }

            error = "Explorer did not create a visible taskbar within the tray-companion startup window.";
            return HasVisibleTaskbar();
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

    private static bool HasVisibleTaskbar() =>
        IsWindowVisible(FindWindow("Shell_TrayWnd", null)) ||
        IsWindowVisible(FindWindow("Shell_SecondaryTrayWnd", null));

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
