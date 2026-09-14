using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace DesktopTuner;

public enum ShellLauncherOperationStatus
{
    Success,
    UnsupportedEdition,
    FeatureNotEnabled,
    UnsupportedLicense,
    ExistingConfiguration,
    AlreadyConfiguredByApp,
    OwnershipChanged,
    OtherMappingsRemain,
    ElevationCancelled,
    Failed
}

public sealed record ShellLauncherOperationResult(ShellLauncherOperationStatus Status, string Message);

public static class ShellLauncherService
{
    private const string ResourceName = "DesktopTuner.ShellLauncherAdmin.ps1";
    private const string ShellLauncherSupportedEditionPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string ShellLauncherStateFileName = "shell-launcher-ownership.json";
    private const int ShellLauncherOptionalFeatureMissingExitCode = 20;
    private const int ExistingShellLauncherConfigurationExitCode = 21;
    private const int ExistingAppConfigurationExitCode = 22;
    private const int OwnershipMismatchExitCode = 24;
    private const int OtherShellLauncherMappingsExitCode = 25;
    private const int UnsupportedShellLauncherLicenseExitCode = 26;

    public static bool IsSupportedEdition(string? editionId) =>
        !string.IsNullOrWhiteSpace(editionId) &&
        (editionId.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase) ||
         editionId.StartsWith("Education", StringComparison.OrdinalIgnoreCase) ||
         editionId.StartsWith("IoTEnterprise", StringComparison.OrdinalIgnoreCase));

    public static bool IsSupportedWindowsEdition()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ShellLauncherSupportedEditionPath, writable: false);
            return IsSupportedEdition(key?.GetValue("EditionID") as string);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceWarning($"Could not read the Windows edition for Shell Launcher support: {ex.Message}");
            return false;
        }
    }

    public static bool HasOwnedConfiguration => File.Exists(GetStatePath());

    public static string BuildShellCommand(string executablePath) =>
        $"{CustomShellPolicy.FormatExecutableCommand(executablePath)} --shell-host";

    public static Task<ShellLauncherOperationResult> ConfigureCurrentUserAsync() => RunElevatedAsync("configure");

    public static Task<ShellLauncherOperationResult> RestoreCurrentUserAsync() => RunElevatedAsync("restore");

    private static async Task<ShellLauncherOperationResult> RunElevatedAsync(string operation)
    {
        if (!IsSupportedWindowsEdition())
            return new ShellLauncherOperationResult(ShellLauncherOperationStatus.UnsupportedEdition, "Windows Shell Launcher is available on Enterprise, Education, and IoT Enterprise editions.");
        if (operation == "configure" && !string.IsNullOrWhiteSpace(CustomShellPolicy.ReadCurrentUserShellCommand()))
            return new ShellLauncherOperationResult(ShellLauncherOperationStatus.ExistingConfiguration, "A per-user alternate shell is already configured. Restore that policy before setting up Shell Launcher.");
        if (Environment.ProcessPath is not { } executablePath || WindowsIdentity.GetCurrent().User is not { } userSid)
            return new ShellLauncherOperationResult(ShellLauncherOperationStatus.Failed, "Could not determine the current executable path or Windows user SID.");

        var shellCommand = BuildShellCommand(executablePath);
        var statePath = GetStatePath();
        var errorPath = GetErrorPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(errorPath)!);
            File.Delete(errorPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ShellLauncherOperationResult(ShellLauncherOperationStatus.Failed, $"Could not prepare Shell Launcher recovery state: {ex.Message}");
        }
        string script;
        try
        {
            script = ReadScriptTemplate()
                .Replace("__OPERATION__", operation, StringComparison.Ordinal)
                .Replace("__SID_BASE64__", EncodeUtf8(userSid.Value), StringComparison.Ordinal)
                .Replace("__SHELL_BASE64__", EncodeUtf8(shellCommand), StringComparison.Ordinal)
                .Replace("__STATE_PATH_BASE64__", EncodeUtf8(statePath), StringComparison.Ordinal)
                .Replace("__ERROR_PATH_BASE64__", EncodeUtf8(errorPath), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"Could not prepare the elevated Shell Launcher script: {ex}");
            return new ShellLauncherOperationResult(ShellLauncherOperationStatus.Failed, ex.Message);
        }
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershellPath = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo(powershellPath)
        {
            Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encodedScript}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var elevatedProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not start the elevated Shell Launcher configuration process.");
            await elevatedProcess.WaitForExitAsync();
            var result = MapExitCode(operation, elevatedProcess.ExitCode);
            if (result.Status == ShellLauncherOperationStatus.Failed && File.Exists(errorPath))
            {
                var detail = File.ReadAllText(errorPath).Trim();
                if (detail.Length > 1200) detail = detail[..1200];
                if (detail.Length > 0) result = result with { Message = $"{result.Message}{Environment.NewLine}{detail}" };
            }
            File.Delete(errorPath);
            return result;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new ShellLauncherOperationResult(ShellLauncherOperationStatus.ElevationCancelled, "Administrator approval was cancelled. Windows Shell Launcher was not changed.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceError($"Could not run the elevated Shell Launcher operation '{operation}': {ex}");
            return new ShellLauncherOperationResult(ShellLauncherOperationStatus.Failed, $"Could not run the elevated Shell Launcher operation: {ex.Message}");
        }
    }

    private static ShellLauncherOperationResult MapExitCode(string operation, int exitCode) => exitCode switch
    {
        0 => new ShellLauncherOperationResult(ShellLauncherOperationStatus.Success,
            operation == "configure"
                ? "Shell Launcher is configured for this user. Other accounts keep Windows Explorer as their default shell. Changes take effect at the next sign-in."
                : "Desktop Tuner's Shell Launcher mapping was restored. Windows Explorer is the sign-in shell again."),
        ShellLauncherOptionalFeatureMissingExitCode => new ShellLauncherOperationResult(ShellLauncherOperationStatus.FeatureNotEnabled, "Enable the optional Windows Shell Launcher feature under Windows Features > Device Lockdown, then try again. Desktop Tuner does not enable system features automatically."),
        UnsupportedShellLauncherLicenseExitCode => new ShellLauncherOperationResult(ShellLauncherOperationStatus.UnsupportedLicense, "Windows reports that this device is not licensed for Shell Launcher. Desktop Tuner left Shell Launcher unchanged."),
        ExistingShellLauncherConfigurationExitCode => new ShellLauncherOperationResult(ShellLauncherOperationStatus.ExistingConfiguration, "Shell Launcher is already enabled or has existing account mappings. Desktop Tuner left that managed configuration unchanged."),
        ExistingAppConfigurationExitCode => new ShellLauncherOperationResult(ShellLauncherOperationStatus.AlreadyConfiguredByApp, "Desktop Tuner already has a saved Shell Launcher configuration. Use Restore Shell Launcher before configuring it again."),
        OwnershipMismatchExitCode => new ShellLauncherOperationResult(ShellLauncherOperationStatus.OwnershipChanged, "The Shell Launcher mapping no longer matches Desktop Tuner's saved configuration. It was left unchanged."),
        OtherShellLauncherMappingsExitCode => new ShellLauncherOperationResult(ShellLauncherOperationStatus.OtherMappingsRemain, "Desktop Tuner's mapping was removed, but other Shell Launcher mappings or a changed default remain. Shell Launcher was left enabled; review it in Windows policy before retrying restore."),
        _ => new ShellLauncherOperationResult(ShellLauncherOperationStatus.Failed, $"The elevated Windows Shell Launcher operation failed with exit code {exitCode}. Check the Windows event log for the WMI provider error.")
    };

    private static string ReadScriptTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The embedded Shell Launcher configuration script is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string GetStatePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopTuner",
        ShellLauncherStateFileName);

    private static string GetErrorPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopTuner",
        "shell-launcher-operation-error.txt");

    private static string EncodeUtf8(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
