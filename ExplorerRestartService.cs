using System.Diagnostics;
using System.IO;

namespace DesktopTuner;

public static class ExplorerRestartService
{
    public static bool TryRestart(out string? error)
    {
        error = null;
        try
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                using (process)
                {
                    if (process.HasExited) continue;
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }

            return Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }
}
