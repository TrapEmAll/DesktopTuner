using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public sealed class ExplorerTrayCompanionService : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private Process? _ownedProcess;

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
    }

    private static bool HasVisibleTaskbar() =>
        IsWindowVisible(FindWindow("Shell_TrayWnd", null)) ||
        IsWindowVisible(FindWindow("Shell_SecondaryTrayWnd", null));

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
