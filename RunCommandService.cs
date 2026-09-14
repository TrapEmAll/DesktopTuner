using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class RunCommandService
{
    public static ProcessStartInfo CreateStartInfo(string commandLine, bool runAsAdministrator = false)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            throw new ArgumentException("Enter a program, folder, document, or Internet address to open.", nameof(commandLine));

        var expandedCommand = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        var arguments = ParseCommandLine(expandedCommand);
        if (arguments.Count == 0 || string.IsNullOrWhiteSpace(arguments[0]))
            throw new ArgumentException("Enter a program, folder, document, or Internet address to open.", nameof(commandLine));

        var startInfo = new ProcessStartInfo(arguments[0]) { UseShellExecute = true };
        foreach (var argument in arguments.Skip(1)) startInfo.ArgumentList.Add(argument);
        if (runAsAdministrator) startInfo.Verb = "runas";
        return startInfo;
    }

    public static IReadOnlyList<string> ParseCommandLine(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        if (string.IsNullOrWhiteSpace(commandLine)) return [];

        var pointer = CommandLineToArgvW(commandLine, out var count);
        if (pointer == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not parse this command.");
        try
        {
            var arguments = new string[count];
            for (var index = 0; index < count; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(pointer, index * IntPtr.Size);
                arguments[index] = Marshal.PtrToStringUni(argumentPointer) ?? string.Empty;
            }
            return arguments;
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CommandLineToArgvW")]
    private static extern nint CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
