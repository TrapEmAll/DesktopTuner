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
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var startInBackground = e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        _instanceMutex = new Mutex(initiallyOwned: true, name: @"Local\DesktopTuner.Singleton", out var createdNew);
        if (!createdNew)
        {
            if (!startInBackground && !DesktopTuner.MainWindow.TryActivateExistingInstance(showSettings: true))
                MessageBox.Show("Desktop Tuner is already running, but its settings window could not be reached.", "Desktop Tuner", MessageBoxButton.OK, MessageBoxImage.Information);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        var window = new MainWindow(startInBackground);
        MainWindow = window;
        window.Show();
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

