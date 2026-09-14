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

        var shellHostArgument = ShellHostLaunchPolicy.IsShellHostInvocation(e.Args);
        var customShellPolicyTargetsApp = CustomShellPolicy.TargetsExecutable(CustomShellPolicy.ReadCurrentUserShellCommand(), Environment.ProcessPath);
        if (customShellPolicyTargetsApp && !shellHostArgument)
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

            _launchExplorerOnShellHostExit = ShellHostLaunchPolicy.ShouldLaunchExplorerOnShellHostExit(shellHostMode, customShellPolicyTargetsApp);
            if (_launchExplorerOnShellHostExit)
                SystemEvents.SessionEnding += OnSystemSessionEnding;

            if (shellOverlayMode)
            {
                var recoveredOverlayTaskbars = NativeTaskbarVisibilityService.RestoreOrphanedSnapshots();
                if (recoveredOverlayTaskbars > 0)
                    System.Diagnostics.Trace.TraceWarning($"Recovered {recoveredOverlayTaskbars} orphaned Windows taskbar visibility snapshot(s) before starting shell overlay mode.");
            }

            var desktopHost = new DesktopHostWindow();
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

        var hasFolderShellInvocation = FolderShellIntegrationService.TryReadInvocation(e.Args, out var folderShellPath);

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

        var restartsUsed = 0;
        while (true)
        {
            int exitCode;
            try
            {
                var startInfo = new ProcessStartInfo(executablePath) { UseShellExecute = false };
                startInfo.ArgumentList.Add("--shell-host");
                using var shellHost = Process.Start(startInfo);
                if (shellHost is null) throw new InvalidOperationException("Windows did not start the Desktop Tuner shell host.");
                await shellHost.WaitForExitAsync();
                exitCode = shellHost.ExitCode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Trace.TraceError($"Could not start or monitor the Desktop Tuner custom shell: {ex}");
                StartExplorerFallback();
                return;
            }

            if (_sessionEnding)
            {
                Shutdown();
                return;
            }
            if (CustomShellPolicy.ShouldRestartHost(exitCode, restartsUsed))
            {
                restartsUsed++;
                await Task.Delay(750);
                continue;
            }

            if (exitCode != 0)
                Trace.TraceError($"Desktop Tuner shell host exited with code {exitCode} after {restartsUsed} restart attempt(s); starting Explorer for recovery.");
            StartExplorerFallback();
            return;
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

