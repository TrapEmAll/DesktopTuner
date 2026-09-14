$ErrorActionPreference = 'Stop'

function Decode-Value([string]$value) {
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value))
}

function Get-WmiReturnCode($result) {
    if ($null -eq $result) { return [uint32]0 }
    $returnValue = $result.PSObject.Properties['ReturnValue']
    if ($null -ne $returnValue) { return [uint32]$returnValue.Value }
    return [uint32]$result
}

$operation = '__OPERATION__'
$sid = Decode-Value '__SID_BASE64__'
$shellCommand = Decode-Value '__SHELL_BASE64__'
$statePath = Decode-Value '__STATE_PATH_BASE64__'
$errorPath = Decode-Value '__ERROR_PATH_BASE64__'
$namespace = 'root\standardcimv2\embedded'
$explorerShell = 'explorer.exe'
$explorerAction = 0

function Test-ShellLauncherLicense {
    $source = @'
using System;
using System.Runtime.InteropServices;
public static class DesktopTunerShellLauncherLicense {
    [DllImport("Slc.dll")]
    private static extern int SLGetWindowsInformationDWORD([MarshalAs(UnmanagedType.LPWStr)] string name, out int value);

    public static bool IsEnabled() {
        int enabled;
        return SLGetWindowsInformationDWORD("EmbeddedFeature-ShellLauncher-Enabled", out enabled) == 0 && enabled != 0;
    }
}
'@
    Add-Type -TypeDefinition $source -ErrorAction Stop
    return [DesktopTunerShellLauncherLicense]::IsEnabled()
}

try {
    $deviceLockdown = Get-WindowsOptionalFeature -Online -FeatureName 'Client-DeviceLockdown'
    $shellLauncherFeature = Get-WindowsOptionalFeature -Online -FeatureName 'Client-EmbeddedShellLauncher'
    if ($deviceLockdown.State -ne 'Enabled' -or $shellLauncherFeature.State -ne 'Enabled') { exit 20 }
    if (-not (Test-ShellLauncherLicense)) { exit 26 }

    $shellLauncher = [wmiclass]"\\localhost\$($namespace):WESL_UserSetting"
    if ($null -eq $shellLauncher) { exit 20 }

    if ($operation -eq 'configure') {
        if (Test-Path -LiteralPath $statePath) { exit 22 }

        $enabledResult = $shellLauncher.IsEnabled()
        if ((Get-WmiReturnCode $enabledResult) -ne 0) { throw "IsEnabled failed with WMI code $(Get-WmiReturnCode $enabledResult)." }
        $wasEnabled = [bool]$enabledResult.Enabled
        $existingMappings = @(Get-WmiObject -Namespace $namespace -Class WESL_UserSetting)
        if ($wasEnabled -or $existingMappings.Count -ne 0) { exit 21 }

        $defaultResult = $shellLauncher.GetDefaultShell()
        if ((Get-WmiReturnCode $defaultResult) -ne 0) { throw "GetDefaultShell failed with WMI code $(Get-WmiReturnCode $defaultResult)." }
        $backup = [pscustomobject]@{
            Sid = $sid
            ShellCommand = $shellCommand
            OriginalDefaultShell = [string]$defaultResult.Shell
            OriginalDefaultAction = [int]$defaultResult.DefaultAction
            WasEnabled = $wasEnabled
        }
        $stateDirectory = Split-Path -Parent $statePath
        New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
        $backup | ConvertTo-Json -Compress | Set-Content -LiteralPath $statePath -Encoding UTF8

        $mappingCreated = $false
        try {
            $result = $shellLauncher.SetCustomShell($sid, $shellCommand, @([int]0), @([int]3), [int]0)
            if ((Get-WmiReturnCode $result) -ne 0) { throw "SetCustomShell failed with WMI code $(Get-WmiReturnCode $result)." }
            $mappingCreated = $true

            $result = $shellLauncher.SetDefaultShell($explorerShell, [int]$explorerAction)
            if ((Get-WmiReturnCode $result) -ne 0) { throw "SetDefaultShell failed with WMI code $(Get-WmiReturnCode $result)." }
            $result = $shellLauncher.SetEnabled($true)
            if ((Get-WmiReturnCode $result) -ne 0) { throw "SetEnabled failed with WMI code $(Get-WmiReturnCode $result)." }
            exit 0
        }
        catch {
            $setupFailure = $_.Exception.Message
            $rollbackErrors = @()
            try {
                $currentMappings = @(Get-WmiObject -Namespace $namespace -Class WESL_UserSetting | Where-Object { $_.Sid -eq $sid })
                if ($currentMappings.Count -eq 1 -and $currentMappings[0].Shell -eq $shellCommand) {
                    $result = $shellLauncher.RemoveCustomShell($sid)
                    if ((Get-WmiReturnCode $result) -ne 0) { throw "RemoveCustomShell failed with WMI code $(Get-WmiReturnCode $result)." }
                }
                elseif ($mappingCreated -or $currentMappings.Count -gt 0) { throw 'The current user mapping changed during setup and was left untouched.' }
            }
            catch { $rollbackErrors += $_.Exception.Message }

            try {
                $currentDefault = $shellLauncher.GetDefaultShell()
                if ((Get-WmiReturnCode $currentDefault) -ne 0) { throw "GetDefaultShell failed with WMI code $(Get-WmiReturnCode $currentDefault)." }
                $isSetupDefault = [string]::Equals([string]$currentDefault.Shell, $explorerShell, [StringComparison]::OrdinalIgnoreCase) -and [int]$currentDefault.DefaultAction -eq $explorerAction
                $isOriginalDefault = [string]::Equals([string]$currentDefault.Shell, [string]$backup.OriginalDefaultShell, [StringComparison]::OrdinalIgnoreCase) -and [int]$currentDefault.DefaultAction -eq [int]$backup.OriginalDefaultAction
                if ($isSetupDefault) {
                    $result = $shellLauncher.SetDefaultShell($backup.OriginalDefaultShell, [int]$backup.OriginalDefaultAction)
                    if ((Get-WmiReturnCode $result) -ne 0) { throw "Restoring the previous default shell failed with WMI code $(Get-WmiReturnCode $result)." }
                }
                elseif (-not $isOriginalDefault) { throw 'The default shell changed during setup and was left untouched.' }
            }
            catch { $rollbackErrors += $_.Exception.Message }

            try {
                $currentEnabled = $shellLauncher.IsEnabled()
                if ((Get-WmiReturnCode $currentEnabled) -ne 0) { throw "IsEnabled failed with WMI code $(Get-WmiReturnCode $currentEnabled)." }
                if ([bool]$currentEnabled.Enabled -ne $wasEnabled) {
                    $result = $shellLauncher.SetEnabled($wasEnabled)
                    if ((Get-WmiReturnCode $result) -ne 0) { throw "Restoring Shell Launcher enablement failed with WMI code $(Get-WmiReturnCode $result)." }
                }
            }
            catch { $rollbackErrors += $_.Exception.Message }

            if ($rollbackErrors.Count -eq 0) { Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue }
            if ($rollbackErrors.Count -gt 0) { throw "Setup failed: $setupFailure Automatic rollback was incomplete: $($rollbackErrors -join '; '). Use Restore Shell Launcher to continue recovery." }
            throw $setupFailure
        }
    }

    if ($operation -eq 'restore') {
        if (-not (Test-Path -LiteralPath $statePath)) { exit 24 }
        $backup = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($backup.Sid -ne $sid -or $backup.ShellCommand -ne $shellCommand) { exit 24 }

        $mappings = @(Get-WmiObject -Namespace $namespace -Class WESL_UserSetting)
        $currentMapping = @($mappings | Where-Object { $_.Sid -eq $sid })
        if ($currentMapping.Count -gt 1) { exit 24 }
        if ($currentMapping.Count -eq 1 -and $currentMapping[0].Shell -ne $shellCommand) { exit 24 }

        if ($currentMapping.Count -eq 1) {
            $result = $shellLauncher.RemoveCustomShell($sid)
            if ((Get-WmiReturnCode $result) -ne 0) { throw "RemoveCustomShell failed with WMI code $(Get-WmiReturnCode $result)." }
        }

        $otherMappings = @($mappings | Where-Object { $_.Sid -ne $sid })
        if ($otherMappings.Count -gt 0) { exit 25 }

        $currentDefault = $shellLauncher.GetDefaultShell()
        if ((Get-WmiReturnCode $currentDefault) -ne 0) { throw "GetDefaultShell failed with WMI code $(Get-WmiReturnCode $currentDefault)." }
        $stillOurs = [string]::Equals([string]$currentDefault.Shell, $explorerShell, [StringComparison]::OrdinalIgnoreCase) -and [int]$currentDefault.DefaultAction -eq $explorerAction
        $alreadyRestored = [string]::Equals([string]$currentDefault.Shell, [string]$backup.OriginalDefaultShell, [StringComparison]::OrdinalIgnoreCase) -and [int]$currentDefault.DefaultAction -eq [int]$backup.OriginalDefaultAction
        if (-not $stillOurs -and -not $alreadyRestored) { exit 25 }

        if ($stillOurs) {
            $result = $shellLauncher.SetDefaultShell([string]$backup.OriginalDefaultShell, [int]$backup.OriginalDefaultAction)
            if ((Get-WmiReturnCode $result) -ne 0) { throw "Restoring the previous default shell failed with WMI code $(Get-WmiReturnCode $result)." }
        }
        $enabledResult = $shellLauncher.IsEnabled()
        if ((Get-WmiReturnCode $enabledResult) -ne 0) { throw "IsEnabled failed with WMI code $(Get-WmiReturnCode $enabledResult)." }
        if ([bool]$enabledResult.Enabled -ne [bool]$backup.WasEnabled) {
            $result = $shellLauncher.SetEnabled([bool]$backup.WasEnabled)
            if ((Get-WmiReturnCode $result) -ne 0) { throw "Restoring Shell Launcher enablement failed with WMI code $(Get-WmiReturnCode $result)." }
        }
        Remove-Item -LiteralPath $statePath -Force
        exit 0
    }

    exit 1
}
catch {
    try { [IO.File]::WriteAllText($errorPath, ($_ | Out-String)) }
    catch { Write-Error "Could not write Shell Launcher error details: $($_.Exception.Message)" -ErrorAction Continue }
    exit 1
}
