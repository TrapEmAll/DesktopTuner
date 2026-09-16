using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace DesktopTuner;

public static class FolderShellIntegrationService
{
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    public const string OpenFolderArgument = "--open-folder";
    public const string OpenFileLocationArgument = "--open-file-location";
    public const string OpenShellLocationArgument = "--open-shell-location";
    private const string DirectoryVerbPath = @"Software\Classes\Directory\shell\DesktopTuner.OpenWith";
    private const string DirectoryBackgroundVerbPath = @"Software\Classes\Directory\Background\shell\DesktopTuner.OpenWith";
    private const string DirectoryShellPath = @"Software\Classes\Directory\shell";
    private const string FolderShellPath = @"Software\Classes\Folder\shell";
    private const string DriveShellPath = @"Software\Classes\Drive\shell";
    private const string FolderVerbPath = @"Software\Classes\Folder\shell\DesktopTuner.OpenWith";
    private const string FolderBackgroundVerbPath = @"Software\Classes\Folder\Background\shell\DesktopTuner.OpenWith";
    private const string DriveVerbPath = @"Software\Classes\Drive\shell\DesktopTuner.OpenWith";
    private const string AssociationStatePath = @"Software\DesktopTuner\FolderShellIntegration";
    private const string PreviousDefaultValue = "PreviousDefaultVerb";
    private const string HadPreviousDefaultValue = "HadPreviousDefaultVerb";
    private const string HadPreviousFolderDefaultValue = "HadPreviousFolderDefaultVerb";
    private const string PreviousFolderDefaultValue = "PreviousFolderDefaultVerb";
    private const string HadPreviousDriveDefaultValue = "HadPreviousDriveDefaultVerb";
    private const string PreviousDriveDefaultValue = "PreviousDriveDefaultVerb";
    public const string DefaultVerb = "DesktopTuner.OpenWith";

    private sealed record RegistryValueSnapshot(object? Value, RegistryValueKind Kind);

    private sealed record RegistryKeySnapshot(
        IReadOnlyDictionary<string, RegistryValueSnapshot> Values,
        IReadOnlyDictionary<string, RegistryKeySnapshot> SubKeys);

    public static IReadOnlyList<string> VerbPaths { get; } =
        [DirectoryVerbPath, DirectoryBackgroundVerbPath, FolderVerbPath, FolderBackgroundVerbPath, DriveVerbPath];

    public static string DefaultVerbPath => DirectoryVerbPath;

    public static IReadOnlyList<string> DefaultHandlerPaths { get; } = [DirectoryShellPath, FolderShellPath, DriveShellPath];

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

    public static bool TryReadFileLocationInvocation(IReadOnlyList<string> arguments, out string filePath)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        filePath = string.Empty;
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (!string.Equals(arguments[index], OpenFileLocationArgument, StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = Environment.ExpandEnvironmentVariables(arguments[index + 1]);
            if (!Path.IsPathFullyQualified(candidate) || !File.Exists(candidate)) return false;
            filePath = Path.GetFullPath(candidate);
            return true;
        }
        return false;
    }

    public static string BuildFileLocationCommand(string executablePath, string shellPathToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (shellPathToken is not ("%1" or "%V")) throw new ArgumentOutOfRangeException(nameof(shellPathToken));
        return $"\"{Path.GetFullPath(executablePath)}\" {OpenFileLocationArgument} \"{shellPathToken}\"";
    }

    public static bool TryReadShellLocationInvocation(IReadOnlyList<string> arguments, out string shellLocation)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        shellLocation = string.Empty;
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (!string.Equals(arguments[index], OpenShellLocationArgument, StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = Environment.ExpandEnvironmentVariables(arguments[index + 1]).Trim();
            if (!IsShellLocationInvocationTarget(candidate)) return false;
            shellLocation = Path.IsPathFullyQualified(candidate) ? Path.GetFullPath(candidate) : candidate;
            return true;
        }
        return false;
    }

    public static bool IsShellLocationInvocationTarget(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (DesktopShellNamespaceCatalog.IsShellNamespaceLocation(value) || Path.IsPathFullyQualified(value));

    public static string BuildShellLocationCommand(string executablePath, string shellPathToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (shellPathToken is not ("%1" or "%V")) throw new ArgumentOutOfRangeException(nameof(shellPathToken));
        return $"\"{Path.GetFullPath(executablePath)}\" {OpenShellLocationArgument} \"{shellPathToken}\"";
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
        var snapshots = VerbPaths.ToDictionary(path => path, CaptureSubKey);
        try
        {
            RegisterVerb(DirectoryVerbPath, "Open with Desktop Tuner", fullExecutablePath, "%1");
            RegisterVerb(DirectoryBackgroundVerbPath, "Browse this folder with Desktop Tuner", fullExecutablePath, "%V");
            RegisterVerb(FolderVerbPath, "Open namespace with Desktop Tuner", fullExecutablePath, "%1", namespaceCommand: true);
            RegisterVerb(FolderBackgroundVerbPath, "Browse namespace with Desktop Tuner", fullExecutablePath, "%V", namespaceCommand: true);
            RegisterVerb(DriveVerbPath, "Open drive with Desktop Tuner", fullExecutablePath, "%1", namespaceCommand: true);
        }
        catch
        {
            foreach (var snapshot in snapshots.Reverse())
            {
                try { RestoreSubKey(snapshot.Key, snapshot.Value); }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or SecurityException)
                {
                    Trace.TraceError($"Could not roll back the folder shell command at {snapshot.Key}: {rollbackException.Message}");
                }
            }
            throw;
        }
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    public static void SetDefaultHandlerEnabled(string? executablePath = null)
    {
        SetEnabled(true, executablePath);
        using var state = Registry.CurrentUser.CreateSubKey(AssociationStatePath, writable: true)
            ?? throw new IOException("Could not open Desktop Tuner's saved folder-handler state.");
        var targets = new[]
        {
            (Path: DirectoryShellPath, HadPrevious: HadPreviousDefaultValue, Previous: PreviousDefaultValue),
            (Path: FolderShellPath, HadPrevious: HadPreviousFolderDefaultValue, Previous: PreviousFolderDefaultValue),
            (Path: DriveShellPath, HadPrevious: HadPreviousDriveDefaultValue, Previous: PreviousDriveDefaultValue)
        };
        var changedTargets = new List<(string Path, string HadPrevious, string Previous)>();
        try
        {
            foreach (var target in targets)
            {
                using var shell = Registry.CurrentUser.CreateSubKey(target.Path, writable: true)
                    ?? throw new IOException($"Could not open Windows' per-user shell settings at {target.Path}.");
                if (state.GetValue(target.HadPrevious) is not null)
                {
                    if (string.Equals(shell.GetValue(null) as string, DefaultVerb, StringComparison.OrdinalIgnoreCase)) continue;
                    throw new InvalidOperationException($"Windows' default shell handler at {target.Path} was changed while Desktop Tuner was active.");
                }

                var previous = shell.GetValue(null) as string;
                state.SetValue(target.HadPrevious, true, RegistryValueKind.DWord);
                state.SetValue(target.Previous, previous ?? string.Empty, RegistryValueKind.String);
                shell.SetValue(null, DefaultVerb, RegistryValueKind.String);
                changedTargets.Add(target);
            }
        }
        catch
        {
            foreach (var target in changedTargets.AsEnumerable().Reverse())
            {
                try
                {
                    using var shell = Registry.CurrentUser.OpenSubKey(target.Path, writable: true);
                    if (shell is null || !string.Equals(shell.GetValue(null) as string, DefaultVerb, StringComparison.OrdinalIgnoreCase)) continue;
                    var previous = state.GetValue(target.Previous) as string;
                    if (!string.IsNullOrEmpty(previous)) shell.SetValue(null, previous, RegistryValueKind.String);
                    else shell.DeleteValue(string.Empty, throwOnMissingValue: false);
                    state.DeleteValue(target.HadPrevious, throwOnMissingValue: false);
                    state.DeleteValue(target.Previous, throwOnMissingValue: false);
                }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or SecurityException)
                {
                    Trace.TraceError($"Could not roll back the shell handler at {target.Path}: {rollbackException.Message}");
                }
            }
            throw;
        }
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    public static bool RestoreDefaultHandler()
    {
        using var state = Registry.CurrentUser.OpenSubKey(AssociationStatePath, writable: false);
        if (state is null) return false;
        var targets = new[]
        {
            (Path: DirectoryShellPath, HadPrevious: HadPreviousDefaultValue, Previous: PreviousDefaultValue),
            (Path: FolderShellPath, HadPrevious: HadPreviousFolderDefaultValue, Previous: PreviousFolderDefaultValue),
            (Path: DriveShellPath, HadPrevious: HadPreviousDriveDefaultValue, Previous: PreviousDriveDefaultValue)
        };
        var restoredAny = false;
        var ownershipMismatch = false;
        foreach (var target in targets)
        {
            if (state.GetValue(target.HadPrevious) is null) continue;
            using var shell = Registry.CurrentUser.OpenSubKey(target.Path, writable: true);
            if (shell is null)
            {
                ownershipMismatch = true;
                continue;
            }
            var current = shell.GetValue(null) as string;
            if (!string.Equals(current, DefaultVerb, StringComparison.OrdinalIgnoreCase))
            {
                var previousValue = state.GetValue(target.Previous) as string;
                var alreadyRestored = string.IsNullOrEmpty(previousValue)
                    ? string.IsNullOrEmpty(current)
                    : string.Equals(current, previousValue, StringComparison.OrdinalIgnoreCase);
                if (!alreadyRestored) ownershipMismatch = true;
                continue;
            }

            var previous = state.GetValue(target.Previous) as string;
            if (!string.IsNullOrEmpty(previous)) shell.SetValue(null, previous, RegistryValueKind.String);
            else shell.DeleteValue(string.Empty, throwOnMissingValue: false);
            restoredAny = true;
        }
        if (ownershipMismatch)
        {
            if (restoredAny) SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            return false;
        }
        Registry.CurrentUser.DeleteSubKeyTree(AssociationStatePath, throwOnMissingSubKey: false);
        if (restoredAny) SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private static void RegisterVerb(string verbPath, string label, string executablePath, string shellPathToken, bool namespaceCommand = false)
    {
        using var verb = Registry.CurrentUser.CreateSubKey(verbPath, writable: true)
            ?? throw new IOException($"Could not register the Desktop Tuner folder command at {verbPath}.");
        verb.SetValue(null, label, RegistryValueKind.String);
        verb.SetValue("Icon", executablePath, RegistryValueKind.String);
        using var command = verb.CreateSubKey("command", writable: true)
            ?? throw new IOException($"Could not register the Desktop Tuner folder command at {verbPath}.");
        command.SetValue(null, namespaceCommand
            ? BuildShellLocationCommand(executablePath, shellPathToken)
            : BuildCommand(executablePath, shellPathToken), RegistryValueKind.String);
    }

    private static RegistryKeySnapshot? CaptureSubKey(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
        return key is null ? null : CaptureSubKey(key);
    }

    private static RegistryKeySnapshot CaptureSubKey(RegistryKey key)
    {
        var values = key.GetValueNames().ToDictionary(
            name => name,
            name => new RegistryValueSnapshot(
                key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames),
                key.GetValueKind(name)));
        var subKeys = key.GetSubKeyNames().ToDictionary(
            name => name,
            name =>
            {
                using var child = key.OpenSubKey(name, writable: false)
                    ?? throw new IOException($"Could not read the existing folder shell command at {key.Name}\\{name}.");
                return CaptureSubKey(child);
            });
        return new RegistryKeySnapshot(values, subKeys);
    }

    private static void RestoreSubKey(string path, RegistryKeySnapshot? snapshot)
    {
        Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        if (snapshot is null) return;
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true)
            ?? throw new IOException($"Could not restore the folder shell command at {path}.");
        RestoreSubKey(key, snapshot);
    }

    private static void RestoreSubKey(RegistryKey key, RegistryKeySnapshot snapshot)
    {
        foreach (var value in snapshot.Values)
        {
            if (value.Value.Value is null) continue;
            key.SetValue(value.Key, value.Value.Value, value.Value.Kind);
        }
        foreach (var child in snapshot.SubKeys)
        {
            using var childKey = key.CreateSubKey(child.Key, writable: true)
                ?? throw new IOException($"Could not restore the folder shell command at {key.Name}\\{child.Key}.");
            RestoreSubKey(childKey, child.Value);
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
