using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ComponentModel;

namespace DesktopTuner;

public static class NativeTaskbarWatchdog
{
    private const string WatchdogArgument = "--taskbar-watchdog";

    public static Process Start(int ownerProcessId, string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the Desktop Tuner executable for taskbar recovery.");
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(WatchdogArgument);
        startInfo.ArgumentList.Add(ownerProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(snapshotPath);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the taskbar recovery process.");
    }

    public static bool IsWatchdogInvocation(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 && string.Equals(arguments[0], WatchdogArgument, StringComparison.OrdinalIgnoreCase);

    public static bool TryReadInvocation(IReadOnlyList<string> arguments, out int ownerProcessId, out string snapshotPath)
    {
        ownerProcessId = 0;
        snapshotPath = string.Empty;
        return arguments.Count == 3 && IsWatchdogInvocation(arguments)
            && int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out ownerProcessId)
            && !string.IsNullOrWhiteSpace(snapshotPath = arguments[2]);
    }

    public static async Task WaitForOwnerAndRestoreAsync(int ownerProcessId, string snapshotPath)
    {
        try
        {
            using var owner = Process.GetProcessById(ownerProcessId);
            await owner.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The owner exited between launching this helper and opening its process handle.
        }
        catch (InvalidOperationException)
        {
            // The process handle is no longer associated with a running owner.
        }
        catch (Win32Exception ex)
        {
            Trace.TraceError($"Could not wait for Desktop Tuner before taskbar recovery: {ex}");
        }

        NativeTaskbarVisibilityService.RestoreSnapshot(snapshotPath);
    }
}
