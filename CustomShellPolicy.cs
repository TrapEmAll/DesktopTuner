using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace DesktopTuner;

public static class CustomShellPolicy
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string RegistryValue = "Shell";
    public const int MaximumHostRestarts = 1;
    public static readonly TimeSpan HostStartupReadinessTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan HostHeartbeatInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan HostHeartbeatTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan HostHeartbeatPollInterval = TimeSpan.FromMilliseconds(500);
    private const string HostReadinessEventName = @"Local\DesktopTuner.CustomShellSupervisor.Ready";
    private const string HostHeartbeatEventName = @"Local\DesktopTuner.CustomShellSupervisor.Heartbeat";

    public static bool IsSupportedEdition(string? editionId) =>
        !string.IsNullOrWhiteSpace(editionId) &&
        (editionId.StartsWith("Professional", StringComparison.OrdinalIgnoreCase) ||
         editionId.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase) ||
         editionId.StartsWith("Education", StringComparison.OrdinalIgnoreCase) ||
         editionId.StartsWith("IoTEnterprise", StringComparison.OrdinalIgnoreCase));

    public static bool IsSupportedWindowsEdition()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            return IsSupportedEdition(key?.GetValue("EditionID") as string);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Could not read the Windows edition for custom-shell policy support: {ex.Message}");
            return false;
        }
    }

    public static string? ReadCurrentUserShellCommand()
    {
        try
        {
            return Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false)?.GetValue(RegistryValue) as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Could not read the per-user Windows custom-shell policy: {ex.Message}");
            return null;
        }
    }

    public static bool TargetsExecutable(string? shellCommand, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(shellCommand) || string.IsNullOrWhiteSpace(executablePath)) return false;
        var configuredExecutable = GetExecutablePath(shellCommand);
        if (configuredExecutable is null) return false;

        try
        {
            var configuredPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredExecutable));
            var currentPath = Path.GetFullPath(executablePath);
            return string.Equals(configuredPath, currentPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    public static bool CanConfigure(string? currentShellCommand, string? executablePath) =>
        !string.IsNullOrWhiteSpace(executablePath) &&
        (string.IsNullOrWhiteSpace(currentShellCommand) || TargetsExecutable(currentShellCommand, executablePath));

    public static bool ShouldRestartHost(int exitCode, int restartsUsed) =>
        exitCode != 0 && restartsUsed < MaximumHostRestarts;

    public static bool ShouldDisablePolicyAfterHostFailure(int exitCode, int restartsUsed) =>
        exitCode != 0 && !ShouldRestartHost(exitCode, restartsUsed);

    public static bool ShouldRestartHostAfterStartupTimeout(int restartsUsed) =>
        restartsUsed < MaximumHostRestarts;

    internal static EventWaitHandle CreateHostReadinessSignal() =>
        new(false, EventResetMode.ManualReset, HostReadinessEventName, out _);

    internal static EventWaitHandle CreateHostHeartbeatSignal() =>
        new(false, EventResetMode.AutoReset, HostHeartbeatEventName, out _);

    internal static void SignalHostReady()
    {
        try
        {
            using var readinessSignal = EventWaitHandle.OpenExisting(HostReadinessEventName);
            readinessSignal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            Trace.TraceWarning("Could not signal custom-shell readiness because the supervisor event is unavailable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceError($"Could not signal custom-shell readiness: {ex}");
        }
    }

    internal static void SignalHostHeartbeat()
    {
        try
        {
            using var heartbeatSignal = EventWaitHandle.OpenExisting(HostHeartbeatEventName);
            heartbeatSignal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            Trace.TraceWarning("Could not signal custom-shell health because the supervisor event is unavailable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceError($"Could not signal custom-shell health: {ex}");
        }
    }

    public static string FormatExecutableCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{Path.GetFullPath(executablePath)}\"";
    }

    public static void ConfigureForExecutable(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullExecutablePath = Path.GetFullPath(executablePath);
        using var existingKey = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        var existingCommand = existingKey?.GetValue(RegistryValue) as string;
        if (TargetsExecutable(existingCommand, fullExecutablePath)) return;
        if (!CanConfigure(existingCommand, fullExecutablePath))
            throw new InvalidOperationException("Another custom shell is already configured for this user. Restore it in Windows policy settings before choosing Desktop Tuner.");

        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new IOException("Could not open the per-user Windows custom-shell policy for writing.");
        key.SetValue(RegistryValue, FormatExecutableCommand(fullExecutablePath), RegistryValueKind.String);
    }

    public static bool RestoreDefaultShell(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
        if (key is null || !TargetsExecutable(key.GetValue(RegistryValue) as string, executablePath)) return false;
        key.DeleteValue(RegistryValue, throwOnMissingValue: false);
        return true;
    }

    private static string? GetExecutablePath(string shellCommand)
    {
        var command = shellCommand.Trim();
        if (command.Length == 0) return null;
        if (command[0] == '"')
        {
            var closingQuote = command.IndexOf('"', 1);
            return closingQuote > 1 ? command[1..closingQuote] : null;
        }

        var executableEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return executableEnd >= 0 ? command[..(executableEnd + 4)].Trim() : command;
    }
}
