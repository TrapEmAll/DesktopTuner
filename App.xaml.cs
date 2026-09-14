using System.Configuration;
using System.Data;
using System.Windows;

namespace DesktopTuner;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;

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

        var shellHostMode = ShellHostLaunchPolicy.IsShellHostInvocation(e.Args);
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
                var shellControls = new MainWindow(shellHostMode: shellHostMode, shellOverlayMode: shellOverlayMode) { ShowInTaskbar = false };
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

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }
}

