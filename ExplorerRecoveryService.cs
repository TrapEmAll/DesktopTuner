using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class ExplorerRecoveryService
{
    private const int MaximumAttempts = 2;
    private static readonly TimeSpan TaskbarTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static bool ShouldRetry(int attempt, bool taskbarReady, int maximumAttempts = MaximumAttempts) =>
        !taskbarReady && attempt > 0 && attempt < maximumAttempts;

    public static bool TryStartExplorer(out string? error)
    {
        error = null;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                using var explorer = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                if (explorer is null)
                    throw new InvalidOperationException("Windows did not start Explorer for shell recovery.");

                if (WaitForTaskbar(TaskbarTimeout)) return true;
                error = "Explorer started but did not create a visible taskbar within the recovery window.";
                if (!ShouldRetry(attempt, taskbarReady: false)) break;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                error = ex.Message;
                if (!ShouldRetry(attempt, taskbarReady: false)) break;
            }
        }

        return false;
    }

    private static bool WaitForTaskbar(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (HasVisibleTaskbar()) return true;
            Thread.Sleep(PollInterval);
        }
        return HasVisibleTaskbar();
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
