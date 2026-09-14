using System.Configuration;
using System.Data;
using System.Windows;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32;

namespace DesktopTuner;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _sessionEnding;
    private bool _launchExplorerOnShellHostExit;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var touchMetrics = TouchTargetPolicy.Resolve(TouchTargetPolicy.HasTouchInput);
        Resources["TouchMenuItemPadding"] = touchMetrics.Padding;
        Resources["TouchMenuItemMinimumHeight"] = touchMetrics.MinimumHeight;
        if (e.Args.Contains("--remove-folder-shell-integration", StringComparer.OrdinalIgnoreCase))
        {
            try { FolderShellIntegrationService.SetEnabled(false); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Could not remove folder context menu commands during uninstall: {ex}"); }
            Shutdown();
            return;
        }
        if (NativeTaskbarWatchdog.IsWatchdogInvocation(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!NativeTaskbarWatchdog.TryReadInvocation(e.Args, out var ownerProcessId, out var snapshotPath))
            {
                Shutdown();
                return;
            }
            WatchTaskbarOwnerAsync(ownerProcessId, snapshotPath);
            return;
        }

        var hasFolderShellInvocation = FolderShellIntegrationService.TryReadInvocation(e.Args, out var folderShellPath);
        var shellHostArgument = ShellHostLaunchPolicy.IsShellHostInvocation(e.Args);
        var shellHostWorkerArgument = ShellHostLaunchPolicy.IsShellHostWorkerInvocation(e.Args);
        var customShellPolicyTargetsApp = CustomShellPolicy.TargetsExecutable(CustomShellPolicy.ReadCurrentUserShellCommand(), Environment.ProcessPath);
        if (hasFolderShellInvocation && DesktopTuner.MainWindow.TryOpenFolderInExistingInstance(folderShellPath))
        {
            Shutdown();
            return;
        }

        var shellHostSupervisorActive = ShellHostLaunchPolicy.ShouldRunShellHostSupervisor(e.Args, customShellPolicyTargetsApp);
        if (shellHostSupervisorActive)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _instanceMutex = new Mutex(initiallyOwned: true, name: @"Local\DesktopTuner.CustomShellSupervisor.Singleton", out var supervisorCreatedNew);
            if (!supervisorCreatedNew)
            {
                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown();
                return;
            }
            SystemEvents.SessionEnding += OnSystemSessionEnding;
            RunCustomShellSupervisor();
            return;
        }

        var shellHostMode = shellHostArgument || customShellPolicyTargetsApp;
        var shellOverlayMode = !shellHostMode && ShellHostLaunchPolicy.IsShellOverlayInvocation(e.Args);
        if (shellHostMode || shellOverlayMode || e.Args.Contains("--desktop-host", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var mutexName = shellHostMode
                ? @"Local\DesktopTuner.ShellHost.Singleton"
                : shellOverlayMode
                    ? @"Local\DesktopTuner.Singleton"
                    : @"Local\DesktopTuner.DesktopHost.Singleton";
            _instanceMutex = new Mutex(initiallyOwned: true, name: mutexName, out var desktopHostCreatedNew);
            if (!desktopHostCreatedNew)
            {
                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown();
                return;
            }

            _launchExplorerOnShellHostExit = ShellHostLaunchPolicy.ShouldLaunchExplorerOnShellHostExit(shellHostMode, shellHostWorkerArgument || customShellPolicyTargetsApp);
            if (_launchExplorerOnShellHostExit)
                SystemEvents.SessionEnding += OnSystemSessionEnding;

            if (shellOverlayMode)
            {
                var recoveredOverlayTaskbars = NativeTaskbarVisibilityService.RestoreOrphanedSnapshots();
                if (recoveredOverlayTaskbars > 0)
                    System.Diagnostics.Trace.TraceWarning($"Recovered {recoveredOverlayTaskbars} orphaned Windows taskbar visibility snapshot(s) before starting shell overlay mode.");
            }

            var desktopHost = new DesktopHostWindow(
                routeFoldersToCompanionExplorer: ShellHostLaunchPolicy.ShouldRouteDesktopFoldersToCompanionExplorer(shellHostMode));
            MainWindow = desktopHost;
            desktopHost.Show();
            if (shellHostMode || shellOverlayMode)
            {
                var shellControls = new MainWindow(
                    shellHostMode: shellHostMode,
                    shellOverlayMode: shellOverlayMode)
                { ShowInTaskbar = false };
                shellControls.Show();
                shellControls.Hide();
            }
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var startInBackground = e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        _instanceMutex = new Mutex(initiallyOwned: true, name: @"Local\DesktopTuner.Singleton", out var createdNew);
        if (!createdNew)
        {
            var activated = hasFolderShellInvocation
                ? DesktopTuner.MainWindow.TryOpenFolderInExistingInstance(folderShellPath)
                : !startInBackground && DesktopTuner.MainWindow.TryActivateExistingInstance(showSettings: true);
            if (!activated && !startInBackground)
                MessageBox.Show("Desktop Tuner is already running, but the requested window could not be reached.", "Desktop Tuner", MessageBoxButton.OK, MessageBoxImage.Information);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        var restoredTaskbars = NativeTaskbarVisibilityService.RestoreOrphanedSnapshots();
        if (restoredTaskbars > 0)
            System.Diagnostics.Trace.TraceWarning($"Recovered {restoredTaskbars} orphaned Windows taskbar visibility snapshot(s) from an earlier Desktop Tuner session.");

        var window = new MainWindow(startInBackground);
        MainWindow = window;
        window.Show();
        if (hasFolderShellInvocation) window.OpenFolderFromShell(folderShellPath);
    }

    private async void WatchTaskbarOwnerAsync(int ownerProcessId, string snapshotPath)
    {
        try { await NativeTaskbarWatchdog.WaitForOwnerAndRestoreAsync(ownerProcessId, snapshotPath); }
        finally { Shutdown(); }
    }

    private async void RunCustomShellSupervisor()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            StartExplorerFallback();
            return;
        }

        EventWaitHandle readinessSignal;
        EventWaitHandle heartbeatSignal;
        try
        {
            readinessSignal = CustomShellPolicy.CreateHostReadinessSignal();
            heartbeatSignal = CustomShellPolicy.CreateHostHeartbeatSignal();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceError($"Could not create the custom-shell readiness signal: {ex}");
            DisableFailedCustomShell(executablePath);
            StartExplorerFallback();
            return;
        }

        using (readinessSignal)
        using (heartbeatSignal)
        {
            var restartsUsed = 0;
            while (true)
            {
                int exitCode;
                var startupTimedOut = false;
                var heartbeatTimedOut = false;
                try
                {
                    readinessSignal.Reset();
                    heartbeatSignal.Reset();
                    var startInfo = new ProcessStartInfo(executablePath) { UseShellExecute = false };
                    startInfo.ArgumentList.Add(ShellHostLaunchPolicy.ShellHostWorkerArgument);
                    using var shellHost = Process.Start(startInfo);
                    if (shellHost is null) throw new InvalidOperationException("Windows did not start the Desktop Tuner shell host.");
                    if (await WaitForShellHostReadyAsync(shellHost, readinessSignal))
                    {
                        if (await WaitForShellHostExitOrHeartbeatTimeoutAsync(shellHost, heartbeatSignal))
                        {
                            exitCode = shellHost.ExitCode;
                        }
                        else
                        {
                            heartbeatTimedOut = true;
                            Trace.TraceError($"Desktop Tuner shell host stopped responding for {CustomShellPolicy.HostHeartbeatTimeout.TotalSeconds:0} seconds; applying the bounded recovery policy.");
                            shellHost.Kill(entireProcessTree: true);
                            await shellHost.WaitForExitAsync();
                            exitCode = -1;
                        }
                    }
                    else if (shellHost.HasExited)
                    {
                        exitCode = shellHost.ExitCode;
                    }
                    else
                    {
                        startupTimedOut = true;
                        Trace.TraceError($"Desktop Tuner shell host did not signal readiness within {CustomShellPolicy.HostStartupReadinessTimeout.TotalSeconds:0} seconds.");
                        shellHost.Kill(entireProcessTree: true);
                        await shellHost.WaitForExitAsync();
                        exitCode = -1;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    Trace.TraceError($"Could not start or monitor the Desktop Tuner custom shell: {ex}");
                    if (CustomShellPolicy.ShouldRestartHost(-1, restartsUsed))
                    {
                        restartsUsed++;
                        await Task.Delay(750);
                        continue;
                    }
                    DisableFailedCustomShell(executablePath);
                    StartExplorerFallback();
                    return;
                }

                if (_sessionEnding)
                {
                    Shutdown();
                    return;
                }
                var shouldRestart = startupTimedOut
                    ? CustomShellPolicy.ShouldRestartHostAfterStartupTimeout(restartsUsed)
                    : CustomShellPolicy.ShouldRestartHost(exitCode, restartsUsed);
                if (shouldRestart)
                {
                    restartsUsed++;
                    await Task.Delay(750);
                    continue;
                }

                if (exitCode != 0)
                {
                    var reason = heartbeatTimedOut ? "stopped responding" : "exited unexpectedly";
                    Trace.TraceError($"Desktop Tuner shell host {reason} (code {exitCode}) after {restartsUsed} restart attempt(s); starting Explorer for recovery.");
                    if (CustomShellPolicy.ShouldDisablePolicyAfterHostFailure(exitCode, restartsUsed))
                        DisableFailedCustomShell(executablePath);
                }
                StartExplorerFallback();
                return;
            }
        }
    }

    private static async Task<bool> WaitForShellHostReadyAsync(Process shellHost, EventWaitHandle readinessSignal)
    {
        var timeout = Stopwatch.StartNew();
        var exitTask = shellHost.WaitForExitAsync();
        while (timeout.Elapsed < CustomShellPolicy.HostStartupReadinessTimeout)
        {
            if (readinessSignal.WaitOne(0)) return true;
            if (await Task.WhenAny(exitTask, Task.Delay(250)) == exitTask) return false;
        }
        return readinessSignal.WaitOne(0);
    }

    private static async Task<bool> WaitForShellHostExitOrHeartbeatTimeoutAsync(Process shellHost, EventWaitHandle heartbeatSignal)
    {
        var lastHeartbeat = Stopwatch.StartNew();
        var exitTask = shellHost.WaitForExitAsync();
        while (!exitTask.IsCompleted)
        {
            if (heartbeatSignal.WaitOne(0)) lastHeartbeat.Restart();
            if (lastHeartbeat.Elapsed >= CustomShellPolicy.HostHeartbeatTimeout) return false;
            var remaining = CustomShellPolicy.HostHeartbeatTimeout - lastHeartbeat.Elapsed;
            var delay = remaining < CustomShellPolicy.HostHeartbeatPollInterval
                ? remaining
                : CustomShellPolicy.HostHeartbeatPollInterval;
            await Task.WhenAny(exitTask, Task.Delay(delay));
        }
        await exitTask;
        return true;
    }

    private static void DisableFailedCustomShell(string executablePath)
    {
        try
        {
            if (CustomShellPolicy.RestoreDefaultShell(executablePath))
                Trace.TraceWarning("Disabled Desktop Tuner's per-user custom-shell policy after shell startup failed; Windows Explorer will remain the shell at the next sign-in.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            Trace.TraceError($"Could not disable the failed Desktop Tuner custom-shell policy: {ex}");
        }
    }

    private void StartExplorerFallback()
    {
        if (_sessionEnding)
        {
            Shutdown();
            return;
        }

        try
        {
            using var explorer = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            if (explorer is null) throw new InvalidOperationException("Windows did not start Explorer for shell recovery.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceError($"Could not start Explorer after the Desktop Tuner shell stopped: {ex}");
        }
        Shutdown();
    }

    private void OnSystemSessionEnding(object? sender, SessionEndingEventArgs e) => _sessionEnding = true;

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionEnding -= OnSystemSessionEnding;
        if (ShellHostLaunchPolicy.ShouldRestoreExplorerAfterShellHostExit(_launchExplorerOnShellHostExit, _sessionEnding, e.ApplicationExitCode))
        {
            try
            {
                using var explorer = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })
                    ?? throw new InvalidOperationException("Windows did not start Explorer for shell recovery.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Trace.TraceError($"Could not start Explorer when leaving the Shell Launcher session: {ex}");
                e.ApplicationExitCode = 1;
            }
        }
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }
}

