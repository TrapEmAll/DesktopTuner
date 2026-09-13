using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTuner;

public sealed record StartupLaunchCommand(string TargetPath, string Arguments, string WorkingDirectory);

public static class StartupShortcutService
{
    private const string ShortcutName = "Desktop Tuner.lnk";

    public static StartupLaunchCommand BuildCommand(string processPath, string entryAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);

        var workingDirectory = Path.GetDirectoryName(entryAssemblyPath)
            ?? throw new ArgumentException("The application assembly must have a directory.", nameof(entryAssemblyPath));
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            return new StartupLaunchCommand(processPath, $"{QuoteArgument(entryAssemblyPath)} --startup", workingDirectory);

        return new StartupLaunchCommand(processPath, "--startup", workingDirectory);
    }

    public static void SetEnabled(bool enabled)
    {
        var startupDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrWhiteSpace(startupDirectory))
            throw new InvalidOperationException("Windows did not provide the current user's Startup folder.");

        var shortcutPath = Path.Combine(startupDirectory, ShortcutName);
        if (!enabled)
        {
            File.Delete(shortcutPath);
            return;
        }

        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Windows could not determine the running application path.");
        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new InvalidOperationException("Windows could not determine the application assembly path.");
        var command = BuildCommand(processPath, assemblyPath);
        Directory.CreateDirectory(startupDirectory);

        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Windows Script Host is unavailable, so the Startup shortcut could not be created.");
            shellObject = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows could not create a Startup shortcut writer.");
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            if (shortcutObject is null) throw new InvalidOperationException("Windows could not create the Startup shortcut.");
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = command.TargetPath;
            shortcut.Arguments = command.Arguments;
            shortcut.WorkingDirectory = command.WorkingDirectory;
            shortcut.Description = "Start the Desktop Tuner taskbar at sign-in.";
            shortcut.WindowStyle = 7;
            shortcut.IconLocation = $"{command.TargetPath},0";
            shortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }

        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
