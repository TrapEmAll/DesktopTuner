using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DesktopTuner;

public partial class DesktopHostWindow : Window
{
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private readonly string _userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private readonly string _publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    private readonly List<FileSystemWatcher> _desktopWatchers = [];
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _isClosed;

    public DesktopHostWindow()
    {
        InitializeComponent();
        Resources["DesktopHostTextShadow"] = new DropShadowEffect { Color = System.Windows.Media.Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9 };
        _refreshTimer.Tick += OnRefreshTimerTick;
        Closed += OnClosed;
        RefreshDesktop();
        StartDesktopWatchers();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DesktopWallpaperService.LoadPrimaryWallpaper() is { } wallpaper)
            Background = new ImageBrush(wallpaper) { Stretch = Stretch.UniformToFill };
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        SetWindowPos(handle, HwndNotTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    private void RefreshDesktop() => DesktopItems.ItemsSource = DesktopHostCatalog.ReadItems([_userDesktop, _publicDesktop]);

    private void StartDesktopWatchers()
    {
        foreach (var root in new[] { _userDesktop, _publicDesktop }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes
                };
                watcher.Created += OnDesktopChanged;
                watcher.Deleted += OnDesktopChanged;
                watcher.Changed += OnDesktopChanged;
                watcher.Renamed += OnDesktopRenamed;
                watcher.Error += OnDesktopWatcherError;
                watcher.EnableRaisingEvents = true;
                _desktopWatchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                System.Diagnostics.Trace.TraceWarning($"Could not monitor desktop folder '{root}': {ex.Message}");
            }
        }
    }

    private void OnDesktopChanged(object sender, FileSystemEventArgs e)
    {
        if (DesktopHostRefreshPolicy.ShouldRefresh(e.ChangeType)) QueueDesktopRefresh();
    }

    private void OnDesktopRenamed(object sender, RenamedEventArgs e) => QueueDesktopRefresh();

    private void OnDesktopWatcherError(object sender, ErrorEventArgs e) =>
        System.Diagnostics.Trace.TraceWarning($"Desktop folder change monitoring reported an error: {e.GetException().Message}");

    private void QueueDesktopRefresh()
    {
        if (_isClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed) return;
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RefreshDesktop();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _refreshTimer.Stop();
        foreach (var watcher in _desktopWatchers) watcher.Dispose();
        _desktopWatchers.Clear();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F5) return;
        RefreshDesktop();
        e.Handled = true;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DesktopHostItem entry }) return;
        try
        {
            var start = new ProcessStartInfo(entry.IsShellNamespace ? "explorer.exe" : entry.FullPath) { UseShellExecute = true };
            if (entry.IsShellNamespace) start.ArgumentList.Add(entry.FullPath);
            Process.Start(start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Could not open desktop item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshDesktop();

    private void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ExplorerFileOperationService.CreateFolder(_userDesktop);
            RefreshDesktop();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Could not create folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenDesktopFolderClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_userDesktop) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Could not open Desktop folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
