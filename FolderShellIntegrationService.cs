using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class FolderShellIntegrationService
{
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    public const string OpenFolderArgument = "--open-folder";
    private const string DirectoryVerbPath = @"Software\Classes\Directory\shell\DesktopTuner.OpenWith";
    private const string DirectoryBackgroundVerbPath = @"Software\Classes\Directory\Background\shell\DesktopTuner.OpenWith";
    private const string DirectoryShellPath = @"Software\Classes\Directory\shell";
    private const string AssociationStatePath = @"Software\DesktopTuner\FolderShellIntegration";
    private const string PreviousDefaultValue = "PreviousDefaultVerb";
    private const string HadPreviousDefaultValue = "HadPreviousDefaultVerb";
    public const string DefaultVerb = "DesktopTuner.OpenWith";

    public static IReadOnlyList<string> VerbPaths { get; } = [DirectoryVerbPath, DirectoryBackgroundVerbPath];

    public static string DefaultVerbPath => DirectoryVerbPath;

    public static bool TryReadInvocation(IReadOnlyList<string> arguments, out string folderPath)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        folderPath = string.Empty;
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (!string.Equals(arguments[index], OpenFolderArgument, StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = Environment.ExpandEnvironmentVariables(arguments[index + 1]);
            if (!Path.IsPathFullyQualified(candidate)) return false;
            folderPath = Path.GetFullPath(candidate);
            return true;
        }
        return false;
    }

    public static string BuildCommand(string executablePath, string shellPathToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (shellPathToken is not ("%1" or "%V")) throw new ArgumentOutOfRangeException(nameof(shellPathToken));
        return $"\"{Path.GetFullPath(executablePath)}\" {OpenFolderArgument} \"{shellPathToken}\"";
    }

    public static void SetEnabled(bool enabled, string? executablePath = null)
    {
        if (!enabled)
        {
            RestoreDefaultHandler();
            foreach (var path in VerbPaths) Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        executablePath ??= Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate Desktop Tuner to register its folder command.");
        var fullExecutablePath = Path.GetFullPath(executablePath);
        RegisterVerb(DirectoryVerbPath, "Open with Desktop Tuner", fullExecutablePath, "%1");
        RegisterVerb(DirectoryBackgroundVerbPath, "Browse this folder with Desktop Tuner", fullExecutablePath, "%V");
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    public static void SetDefaultHandlerEnabled(string? executablePath = null)
    {
        SetEnabled(true, executablePath);
        using var state = Registry.CurrentUser.CreateSubKey(AssociationStatePath, writable: true)
            ?? throw new IOException("Could not open Desktop Tuner's saved folder-handler state.");
        using var directoryShell = Registry.CurrentUser.CreateSubKey(DirectoryShellPath, writable: true)
            ?? throw new IOException("Could not open Windows' per-user folder shell settings.");
        if (state.GetValue(HadPreviousDefaultValue) is not null)
        {
            if (string.Equals(directoryShell.GetValue(null) as string, DefaultVerb, StringComparison.OrdinalIgnoreCase)) return;
            throw new InvalidOperationException("Windows' default folder handler was changed while Desktop Tuner was active.");
        }

        var previous = directoryShell.GetValue(null) as string;
        state.SetValue(HadPreviousDefaultValue, true, RegistryValueKind.DWord);
        state.SetValue(PreviousDefaultValue, previous ?? string.Empty, RegistryValueKind.String);
        directoryShell.SetValue(null, DefaultVerb, RegistryValueKind.String);
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    public static bool RestoreDefaultHandler()
    {
        using var state = Registry.CurrentUser.OpenSubKey(AssociationStatePath, writable: false);
        if (state is null) return false;
        using var directoryShell = Registry.CurrentUser.OpenSubKey(DirectoryShellPath, writable: true);
        if (directoryShell is null) return false;
        var current = directoryShell.GetValue(null) as string;
        if (!string.Equals(current, DefaultVerb, StringComparison.OrdinalIgnoreCase))
        {
            Registry.CurrentUser.DeleteSubKeyTree(AssociationStatePath, throwOnMissingSubKey: false);
            return false;
        }

        var hadPrevious = state.GetValue(HadPreviousDefaultValue) is not null;
        var previous = state.GetValue(PreviousDefaultValue) as string;
        if (hadPrevious && !string.IsNullOrEmpty(previous)) directoryShell.SetValue(null, previous, RegistryValueKind.String);
        else directoryShell.DeleteValue(string.Empty, throwOnMissingValue: false);
        Registry.CurrentUser.DeleteSubKeyTree(AssociationStatePath, throwOnMissingSubKey: false);
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private static void RegisterVerb(string verbPath, string label, string executablePath, string shellPathToken)
    {
        using var verb = Registry.CurrentUser.CreateSubKey(verbPath, writable: true)
            ?? throw new IOException($"Could not register the Desktop Tuner folder command at {verbPath}.");
        verb.SetValue(null, label, RegistryValueKind.String);
        verb.SetValue("Icon", executablePath, RegistryValueKind.String);
        using var command = verb.CreateSubKey("command", writable: true)
            ?? throw new IOException($"Could not register the Desktop Tuner folder command at {verbPath}.");
        command.SetValue(null, BuildCommand(executablePath, shellPathToken), RegistryValueKind.String);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
