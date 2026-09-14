using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace DesktopTuner;

public static class CustomShellPolicy
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string RegistryValue = "Shell";

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
