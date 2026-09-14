using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopTuner;

public partial class DesktopHostWindow : Window
{
    private const string ItemIdentityFormat = "DesktopTuner.DesktopItemIdentity";
    private const double DesktopIconWidth = 100;
    private const double DesktopIconHeight = 112;
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private readonly string _userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private readonly string _publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    private readonly DesktopHostLayoutStore _layoutStore = new();
    private readonly ObservableCollection<DesktopHostItem> _desktopItems = [];
    private readonly List<FileSystemWatcher> _desktopWatchers = [];
    private IReadOnlyList<DesktopHostMonitorViewport> _desktopMonitors = [];
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _isClosed;
    private DesktopHostItem? _dragCandidate;
    private string? _selectionAnchorPath;
    private Point _dragStart;

    public DesktopHostWindow()
    {
        InitializeComponent();
        Resources["DesktopHostTextShadow"] = new DropShadowEffect { Color = System.Windows.Media.Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9 };
        _refreshTimer.Tick += OnRefreshTimerTick;
        Closed += OnClosed;
        SizeChanged += OnSizeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        DesktopItems.ItemsSource = _desktopItems;
        RefreshDesktop();
        StartDesktopWatchers();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOnVirtualDesktop();
        if (DesktopWallpaperService.LoadPrimaryWallpaper() is { } wallpaper)
            Background = new ImageBrush(wallpaper) { Stretch = Stretch.UniformToFill };
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        SetWindowPos(handle, HwndNotTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshDesktop));
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueDesktopRefresh();

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_isClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed) return;
            PositionOnVirtualDesktop();
            QueueDesktopRefresh();
        });
    }

    private void PositionOnVirtualDesktop()
    {
        try
        {
            var displays = TaskbarDisplayService.Enumerate();
            var bounds = DesktopHostDisplayLayoutPolicy.CalculateVirtualBounds(displays);
            if (!TaskbarDisplayService.PositionWindow(this, bounds))
            {
                System.Diagnostics.Trace.TraceWarning("Windows could not position the desktop host across all connected displays.");
                return;
            }

            var scale = TaskbarDisplayService.ReadWindowDpi(displays[0], this);
            Width = bounds.Width / scale.ScaleX;
            Height = bounds.Height / scale.ScaleY;
            TaskbarDisplayService.PositionWindow(this, bounds);
            var canvasWidth = Math.Max(1, Width - DesktopItems.Margin.Left - DesktopItems.Margin.Right);
            var canvasHeight = Math.Max(1, Height - DesktopItems.Margin.Top - DesktopItems.Margin.Bottom);
            _desktopMonitors = DesktopHostDisplayLayoutPolicy.CreateMonitorViewports(displays, bounds, canvasWidth, canvasHeight,
                scale.ScaleX, scale.ScaleY);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not position desktop host across connected displays: {ex.Message}");
        }
    }

    private void RefreshDesktop()
    {
        var selectedPaths = _desktopItems.Where(item => item.IsSelected)
            .Select(item => item.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = DesktopHostCatalog.ReadItems([_userDesktop, _publicDesktop]);
        var ordered = _desktopMonitors.Count > 0
            ? _layoutStore.ApplyMonitorLayout(entries, _desktopMonitors)
            : _layoutStore.ApplyLayout(entries, DesktopItems.ActualWidth, DesktopItems.ActualHeight);
        _desktopItems.Clear();
        foreach (var item in ordered)
        {
            item.IsSelected = selectedPaths.Contains(item.FullPath);
            _desktopItems.Add(item);
        }
        if (_selectionAnchorPath is not null && !_desktopItems.Any(item => string.Equals(item.FullPath, _selectionAnchorPath, StringComparison.OrdinalIgnoreCase)))
            _selectionAnchorPath = null;
    }

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
        SizeChanged -= OnSizeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var watcher in _desktopWatchers) watcher.Dispose();
        _desktopWatchers.Clear();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            RefreshDesktop();
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ApplySelection(DesktopHostSelectionPolicy.SelectAll(_desktopItems), _selectionAnchorPath);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ApplySelection(new HashSet<string>(StringComparer.OrdinalIgnoreCase), null);
            e.Handled = true;
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DesktopHostItem entry }) return;
        OpenDesktopItem(entry);
        e.Handled = true;
    }

    private void OnOpenItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DesktopHostItem entry }) OpenDesktopItem(entry);
    }

    private void OpenDesktopItem(DesktopHostItem entry)
    {
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
    }

    private async void OnShowNativeContextMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: DesktopHostItem { CanShowNativeContextMenu: true } entry }) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            if (entry.IsShellNamespace)
                await NativeShellContextMenuService.ShowForShellItemAsync(owner, entry.FullPath);
            else
                await NativeShellContextMenuService.ShowForItemsAsync(owner, [entry.FullPath]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open Windows' context menu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = (sender as Button)?.DataContext as DesktopHostItem;
        _dragStart = e.GetPosition(this);
        if (_dragCandidate is not { } item) return;
        var modifiers = Keyboard.Modifiers;
        var selection = DesktopHostSelectionPolicy.Select(
            _desktopItems,
            item.FullPath,
            modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Shift),
            _selectionAnchorPath);
        ApplySelection(selection.Paths, selection.AnchorPath);
    }

    private void ApplySelection(IReadOnlySet<string> selectedPaths, string? anchorPath)
    {
        foreach (var item in _desktopItems)
            item.IsSelected = selectedPaths.Contains(item.FullPath);
        _selectionAnchorPath = anchorPath;
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is not { } item || item.IsShellNamespace
            || !File.Exists(item.FullPath) && !Directory.Exists(item.FullPath)) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragCandidate = null;
        var draggedItems = (item.IsSelected ? _desktopItems.Where(candidate => candidate.IsSelected) : [item])
            .Where(candidate => !candidate.IsShellNamespace && (File.Exists(candidate.FullPath) || Directory.Exists(candidate.FullPath)))
            .ToArray();
        if (draggedItems.Length == 0) return;
        var paths = draggedItems.Select(candidate => candidate.FullPath).ToArray();
        var data = new DataObject(DataFormats.FileDrop, paths);
        data.SetData(ItemIdentityFormat, paths.Length == 1 ? paths[0] : paths);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    private void OnItemDragOver(object sender, DragEventArgs e)
    {
        var internalPaths = ReadInternalItemPaths(e.Data);
        if (internalPaths.Count == 1 && _desktopItems.Any(item => string.Equals(item.FullPath, internalPaths[0], StringComparison.OrdinalIgnoreCase)))
            e.Effects = DragDropEffects.Move;
        else if (internalPaths.Count > 0 || !e.Data.GetDataPresent(DataFormats.FileDrop) || !Directory.Exists(_userDesktop) ||
                 e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0 ||
                 !TryResolveDesktopDropMove(paths, e, out var move))
            e.Effects = DragDropEffects.None;
        else e.Effects = move ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnItemDrop(object sender, DragEventArgs e)
    {
        if (sender is not Button { DataContext: DesktopHostItem target }) return;
        if (e.Data.GetDataPresent(ItemIdentityFormat))
        {
            var internalPaths = ReadInternalItemPaths(e.Data);
            if (internalPaths.Count == 1)
            {
                ReorderDesktopItem(internalPaths[0], target.FullPath);
                e.Effects = DragDropEffects.Move;
            }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        TransferDroppedItems(e);
    }

    private void OnDesktopDragOver(object sender, DragEventArgs e)
    {
        var internalPaths = ReadInternalItemPaths(e.Data);
        if (internalPaths.Count > 0 && internalPaths.All(path => _desktopItems.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))))
            e.Effects = DragDropEffects.Move;
        else if (e.Data.GetDataPresent(DataFormats.FileDrop) && Directory.Exists(_userDesktop) &&
                 e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 &&
                 TryResolveDesktopDropMove(paths, e, out var move))
            e.Effects = move ? DragDropEffects.Move : DragDropEffects.Copy;
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDesktopDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ItemIdentityFormat))
        {
            var internalPaths = ReadInternalItemPaths(e.Data);
            var selectedItems = internalPaths.Select(path => _desktopItems.FirstOrDefault(item =>
                string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (selectedItems.Length > 0 && selectedItems.All(item => item is not null))
            {
                var point = e.GetPosition(DesktopItems);
                var anchor = selectedItems[0]!;
                var positions = DesktopHostLayoutStore.TranslateSelection(selectedItems.Select(item => item!), anchor.FullPath,
                    new DesktopHostPosition(point.X - DesktopIconWidth / 2, point.Y - DesktopIconHeight / 2),
                    DesktopItems.ActualWidth, DesktopItems.ActualHeight);
                foreach (var item in selectedItems)
                {
                    if (item is null || !positions.TryGetValue(item.FullPath, out var position)) continue;
                    if (_desktopMonitors.Count > 0) PlaceItemOnMonitor(item, position);
                    else item.SetPosition(position);
                }
                SaveDesktopLayout("Icon moved for this session, but its position could not be saved.");
                e.Effects = DragDropEffects.Move;
            }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        TransferDroppedItems(e);
    }

    private static IReadOnlyList<string> ReadInternalItemPaths(IDataObject data)
    {
        if (!data.GetDataPresent(ItemIdentityFormat)) return [];
        return data.GetData(ItemIdentityFormat) switch
        {
            string path when !string.IsNullOrWhiteSpace(path) => [path],
            string[] paths => paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            _ => []
        };
    }

    private bool TryResolveDesktopDropMove(IEnumerable<string> paths, DragEventArgs e, out bool move)
    {
        try
        {
            move = ExplorerDragDropPolicy.ResolveMove(paths, _userDesktop,
                e.KeyStates.HasFlag(DragDropKeyStates.ControlKey), e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            move = false;
            return false;
        }
    }

    private void TransferDroppedItems(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        try
        {
            if (!TryResolveDesktopDropMove(paths, e, out var move))
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            ExplorerFileOperationService.Transfer(paths, _userDesktop, move);
            RefreshDesktop();
            e.Effects = move ? DragDropEffects.Move : DragDropEffects.Copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            MessageBox.Show(this, ex.Message, "Could not transfer items to Desktop", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Effects = DragDropEffects.None;
        }
    }

    private void ReorderDesktopItem(string sourcePath, string targetPath)
    {
        var sourceIndex = -1;
        var targetIndex = -1;
        for (var index = 0; index < _desktopItems.Count; index++)
        {
            if (string.Equals(_desktopItems[index].FullPath, sourcePath, StringComparison.OrdinalIgnoreCase)) sourceIndex = index;
            if (string.Equals(_desktopItems[index].FullPath, targetPath, StringComparison.OrdinalIgnoreCase)) targetIndex = index;
        }
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

        var source = _desktopItems[sourceIndex];
        var target = _desktopItems[targetIndex];
        var sourcePosition = new DesktopHostPosition(source.Left, source.Top);
        source.SetPosition(new DesktopHostPosition(target.Left, target.Top));
        target.SetPosition(sourcePosition);
        _desktopItems.RemoveAt(sourceIndex);
        _desktopItems.Insert(_desktopItems.IndexOf(target), source);
        SaveDesktopLayout("Icon layout changed for this session, but could not be saved.");
    }

    private void PlaceItemOnMonitor(DesktopHostItem item, DesktopHostPosition position)
    {
        var target = DesktopHostDisplayLayoutPolicy.FindNearestMonitor(_desktopMonitors,
            new DesktopHostPosition(position.Left + DesktopIconWidth / 2, position.Top + DesktopIconHeight / 2));
        var local = new DesktopHostPosition(
            Math.Clamp(position.Left - target.Left, 0, Math.Max(0, target.Width - DesktopIconWidth)),
            Math.Clamp(position.Top - target.Top, 0, Math.Max(0, target.Height - DesktopIconHeight)));
        item.SetPosition(new DesktopHostPosition(target.Left + local.Left, target.Top + local.Top));
        item.MonitorDeviceName = target.DeviceName;
    }

    private void SaveDesktopLayout(string failureMessage)
    {
        var saved = _desktopMonitors.Count > 0
            ? _layoutStore.SaveMonitorLayout(_desktopItems, _desktopMonitors)
            : _layoutStore.SaveLayout(_desktopItems);
        if (!saved)
            MessageBox.Show(this, failureMessage, "Desktop layout", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private async void OnShowDesktopContextMenuClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.ShowForFolderBackgroundAsync(owner, _userDesktop);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open Windows' desktop context menu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
