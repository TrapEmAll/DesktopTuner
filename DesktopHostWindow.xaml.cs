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
    private const int MessageClipboardUpdate = 0x031D;
    private const double DesktopIconWidth = 100;
    private const double DesktopIconHeight = 112;
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private readonly string _userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private readonly string _publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    private readonly DesktopHostLayoutStore _layoutStore = new();
    private readonly bool _routeFoldersToCompanionExplorer;
    private readonly Func<string, bool>? _pinTaskbarItem;
    private readonly Func<string, bool>? _isTaskbarItemPinned;
    private readonly Func<string, bool>? _pinStartItem;
    private readonly Func<string, bool>? _isStartItemPinned;
    private readonly ObservableCollection<DesktopHostItem> _desktopItems = [];
    private readonly List<FileSystemWatcher> _desktopWatchers = [];
    private readonly HashSet<string> _cutParsingNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<DesktopHostMonitorViewport> _desktopMonitors = [];
    private IReadOnlyList<DesktopHostMonitorViewport> _wallpaperMonitors = [];
    private IReadOnlyList<TaskbarDisplay> _connectedDisplays = [];
    private DesktopShellChangeNotificationListener? _shellChangeNotifications;
    private HwndSource? _windowSource;
    private HwndSourceHook? _windowMessageHook;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _wallpaperRefreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _wallpaperSignature;
    private bool _isClosed;
    private bool _clipboardListenerRegistered;
    private uint _cutClipboardSequence;
    private bool _isMarqueeSelecting;
    private bool _marqueeTogglesSelection;
    private DesktopHostItem? _dragCandidate;
    private IReadOnlyList<string>? _activeNativeShellDragPaths;
    private string? _selectionAnchorPath;
    private Point _dragStart;
    private Point _marqueeStart;
    private HashSet<string> _marqueeInitialSelection = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _marqueeSelectionBefore = new(StringComparer.OrdinalIgnoreCase);
    private string? _marqueeAnchorBefore;

    public DesktopHostWindow(bool routeFoldersToCompanionExplorer = false, Func<string, bool>? pinTaskbarItem = null, Func<string, bool>? isTaskbarItemPinned = null, Func<string, bool>? pinStartItem = null, Func<string, bool>? isStartItemPinned = null)
    {
        _routeFoldersToCompanionExplorer = routeFoldersToCompanionExplorer;
        _pinTaskbarItem = pinTaskbarItem;
        _isTaskbarItemPinned = isTaskbarItemPinned;
        _pinStartItem = pinStartItem;
        _isStartItemPinned = isStartItemPinned;
        InitializeComponent();
        Resources["DesktopHostTextShadow"] = new DropShadowEffect { Color = System.Windows.Media.Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9 };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _wallpaperRefreshTimer.Tick += OnWallpaperRefreshTimerTick;
        Closed += OnClosed;
        SizeChanged += OnSizeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        DesktopItems.ItemsSource = _desktopItems;
        RefreshDesktop();
        StartDesktopWatchers();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOnVirtualDesktop();
        ApplyDesktopWallpaper();
        _wallpaperRefreshTimer.Start();
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        SetWindowPos(handle, HwndNotTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        try
        {
            _windowSource = HwndSource.FromHwnd(handle);
            if (_windowSource is not null)
            {
                _windowMessageHook = WindowMessageHook;
                _windowSource.AddHook(_windowMessageHook);
                _shellChangeNotifications = new DesktopShellChangeNotificationListener(_windowSource, QueueDesktopRefresh);
                _clipboardListenerRegistered = AddClipboardFormatListener(handle);
            }
            if (!_clipboardListenerRegistered)
                Trace.TraceWarning($"Could not monitor clipboard changes for desktop cut-state display: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Trace.TraceWarning($"Desktop Shell change notifications are unavailable; filesystem watchers and manual refresh remain active: {ex.Message}");
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshDesktop));
    }

    private void ApplyDesktopWallpaper()
    {
        if (_isClosed) return;
        var snapshot = DesktopWallpaperService.LoadCurrent(_connectedDisplays);
        if (snapshot is null)
        {
            var fallback = DesktopWallpaperService.LoadPrimaryWallpaper();
            var fallbackSignature = fallback?.UriSource?.ToString() ?? "windows-solid-desktop";
            if (string.Equals(_wallpaperSignature, fallbackSignature, StringComparison.OrdinalIgnoreCase)) return;
            _wallpaperSignature = fallbackSignature;
            WallpaperLayer.Children.Clear();
            Background = fallback is null
                ? new SolidColorBrush(Color.FromRgb(39, 48, 68))
                : new ImageBrush(fallback) { Stretch = Stretch.UniformToFill };
            return;
        }

        var signature = $"{snapshot.Position}|{snapshot.BackgroundColor}|" +
            string.Join('|', snapshot.Monitors.OrderBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
                .Select(monitor => $"{monitor.DeviceName}:{monitor.WallpaperPath}:{GetWallpaperTimestamp(monitor.WallpaperPath)}"));
        if (string.Equals(_wallpaperSignature, signature, StringComparison.OrdinalIgnoreCase)) return;
        _wallpaperSignature = signature;
        WallpaperLayer.Children.Clear();
        Background = new SolidColorBrush(snapshot.BackgroundColor);
        var presentation = DesktopWallpaperPresentationPolicy.Resolve(snapshot.Position);

        if (presentation.Span)
        {
            var primaryDevice = _connectedDisplays.FirstOrDefault(display => display.IsPrimary)?.DeviceName;
            var wallpaperPath = snapshot.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.DeviceName, primaryDevice, StringComparison.OrdinalIgnoreCase))?.WallpaperPath
                ?? snapshot.Monitors.FirstOrDefault(monitor => monitor.WallpaperPath is not null)?.WallpaperPath;
            if (wallpaperPath is not null)
            {
                var virtualViewport = new DesktopHostMonitorViewport("virtual", 0, 0,
                    Math.Max(1, WallpaperLayer.ActualWidth), Math.Max(1, WallpaperLayer.ActualHeight), true);
                AddWallpaperLayer(wallpaperPath, virtualViewport, presentation, snapshot.BackgroundColor, 1, 1);
            }
            return;
        }

        foreach (var monitor in snapshot.Monitors)
        {
            if (monitor.WallpaperPath is null) continue;
            var viewport = _wallpaperMonitors.FirstOrDefault(candidate =>
                string.Equals(candidate.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (viewport is null) continue;
            AddWallpaperLayer(monitor.WallpaperPath, viewport, presentation, snapshot.BackgroundColor, monitor.ScaleX, monitor.ScaleY);
        }
    }

    private static long GetWallpaperTimestamp(string? wallpaperPath)
    {
        if (wallpaperPath is null) return 0;
        try { return File.GetLastWriteTimeUtc(wallpaperPath).Ticks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }

    private void AddWallpaperLayer(
        string wallpaperPath,
        DesktopHostMonitorViewport viewport,
        DesktopWallpaperPresentation presentation,
        Color backgroundColor,
        double scaleX,
        double scaleY)
    {
        var bitmap = DesktopWallpaperService.LoadImage(wallpaperPath);
        if (bitmap is null) return;

        var layer = new Border
        {
            Width = viewport.Width,
            Height = viewport.Height,
            Background = new SolidColorBrush(backgroundColor),
            ClipToBounds = true
        };
        if (presentation.Tile)
        {
            layer.Background = new ImageBrush(bitmap)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0,
                    Math.Max(1, bitmap.PixelWidth / Math.Max(0.1, scaleX)),
                    Math.Max(1, bitmap.PixelHeight / Math.Max(0.1, scaleY))),
                Stretch = Stretch.None
            };
        }
        else
        {
            var image = new Image { Source = bitmap, Stretch = presentation.Stretch };
            if (presentation.Stretch == Stretch.None)
            {
                image.HorizontalAlignment = HorizontalAlignment.Center;
                image.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                image.HorizontalAlignment = HorizontalAlignment.Stretch;
                image.VerticalAlignment = VerticalAlignment.Stretch;
            }
            layer.Child = image;
        }

        Canvas.SetLeft(layer, viewport.Left);
        Canvas.SetTop(layer, viewport.Top);
        WallpaperLayer.Children.Add(layer);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueDesktopRefresh();

    private void OnWallpaperRefreshTimerTick(object? sender, EventArgs e) => ApplyDesktopWallpaper();

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.Desktop || _isClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.BeginInvoke(new Action(ApplyDesktopWallpaper));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_isClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed) return;
            PositionOnVirtualDesktop();
            ApplyDesktopWallpaper();
            QueueDesktopRefresh();
        });
    }

    private void PositionOnVirtualDesktop()
    {
        try
        {
            var displays = TaskbarDisplayService.Enumerate();
            _connectedDisplays = displays;
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
            _wallpaperMonitors = DesktopHostDisplayLayoutPolicy.CreateMonitorViewports(displays, bounds, Width, Height,
                scale.ScaleX, scale.ScaleY, contentInset: 0);
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
        var entries = DesktopHostCatalog.ReadItems([_userDesktop, _publicDesktop], includeDesktopNamespace: true);
        var preferences = _layoutStore.ReadPreferences();
        var ordered = _desktopMonitors.Count > 0
            ? _layoutStore.ApplyMonitorLayout(entries, _desktopMonitors)
            : _layoutStore.ApplyLayout(entries, DesktopItems.ActualWidth, DesktopItems.ActualHeight);
        if (_desktopMonitors.Count > 0)
        {
            if (preferences.AutoArrange)
                DesktopHostArrangementPolicy.Arrange(ordered, _desktopMonitors, preferences.SortMode);
            else if (preferences.AlignToGrid)
                DesktopHostArrangementPolicy.AlignToGrid(ordered, _desktopMonitors);
        }
        _desktopItems.Clear();
        foreach (var item in ordered)
        {
            item.IsSelected = selectedPaths.Contains(item.FullPath);
            item.IsCut = _cutParsingNames.Contains(item.FullPath);
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
        _wallpaperRefreshTimer.Stop();
        SizeChanged -= OnSizeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _shellChangeNotifications?.Dispose();
        _shellChangeNotifications = null;
        var handle = new WindowInteropHelper(this).Handle;
        if (_clipboardListenerRegistered) RemoveClipboardFormatListener(handle);
        if (_windowSource is not null && _windowMessageHook is not null) _windowSource.RemoveHook(_windowMessageHook);
        _clipboardListenerRegistered = false;
        _windowSource = null;
        _windowMessageHook = null;
        foreach (var watcher in _desktopWatchers) watcher.Dispose();
        _desktopWatchers.Clear();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var selectedItems = _desktopItems.Where(item => item.IsSelected).ToArray();
        if (DesktopHostKeyboardPolicy.ShouldCreateFolder(e.Key, Keyboard.Modifiers, e.OriginalSource is TextBox))
        {
            e.Handled = true;
            _ = CreateDesktopFolderAsync();
            return;
        }

        var clipboardAction = DesktopHostKeyboardPolicy.ResolveClipboardAction(e.Key, Keyboard.Modifiers,
            selectedItems.Length > 0, e.OriginalSource is TextBox);
        if (clipboardAction != DesktopHostClipboardAction.None)
        {
            e.Handled = true;
            if (clipboardAction == DesktopHostClipboardAction.Copy)
                _ = CopySelectedDesktopItemsAsync(cut: false);
            else if (clipboardAction == DesktopHostClipboardAction.Cut)
                _ = CopySelectedDesktopItemsAsync(cut: true);
            else
                _ = PasteDesktopItemsAsync();
        }
        else if (DesktopHostKeyboardPolicy.ShouldShowProperties(e.Key, Keyboard.Modifiers,
                     selectedItems.Length > 0, e.OriginalSource is TextBox, e.SystemKey))
        {
            e.Handled = true;
            _ = ShowSelectedDesktopPropertiesAsync();
        }
        else if (DesktopHostKeyboardPolicy.ShouldDeleteSelection(e.Key, Keyboard.Modifiers, selectedItems.Length > 0,
                e.OriginalSource is TextBox))
        {
            e.Handled = true;
            _ = DeleteSelectedDesktopItemsAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        else if (e.Key == Key.F5)
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
            if (_isMarqueeSelecting) CancelDesktopMarquee();
            else ApplySelection(new HashSet<string>(StringComparer.OrdinalIgnoreCase), null);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            var selected = _desktopItems.Where(item => item.IsSelected).ToArray();
            var target = selected.Length == 1 ? selected[0]
                : selected.Length == 0 && TryGetFocusedDesktopItem(out _, out var renameFocusedItem) ? renameFocusedItem
                : null;
            if (target is { CanRename: true, IsRenaming: false }) BeginRename(target);
            e.Handled = true;
        }
        else if (TryGetFocusedDesktopItem(out var focusedButton, out var focusedItem) &&
                 TryGetNavigationDirection(e.Key, out var direction))
        {
            var target = DesktopHostSelectionPolicy.FindAdjacentItem(_desktopItems, focusedItem.FullPath, direction);
            if (target is not null)
            {
                var modifiers = Keyboard.Modifiers;
                if (modifiers.HasFlag(ModifierKeys.Shift))
                {
                    var selection = DesktopHostSelectionPolicy.Select(_desktopItems, target.FullPath,
                        modifiers.HasFlag(ModifierKeys.Control), shiftPressed: true, _selectionAnchorPath);
                    ApplySelection(selection.Paths, selection.AnchorPath);
                }
                else if (!modifiers.HasFlag(ModifierKeys.Control))
                {
                    ApplySelection(new HashSet<string>([target.FullPath], StringComparer.OrdinalIgnoreCase), target.FullPath);
                }

                var targetButton = FindVisualChildren<Button>(DesktopItems)
                    .FirstOrDefault(button => ReferenceEquals(button.DataContext, target));
                targetButton?.Focus();
                if (targetButton is not null) Keyboard.Focus(targetButton);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.OriginalSource is not TextBox)
        {
            DesktopHostItem? focusedOpenItem = null;
            if (TryGetFocusedDesktopItem(out _, out var currentFocusedItem)) focusedOpenItem = currentFocusedItem;
            if (DesktopHostOpenPolicy.SelectItems(_desktopItems, focusedOpenItem).Count > 0)
            {
                OpenSelectedDesktopItems(focusedOpenItem);
                e.Handled = true;
            }
        }
        else if (DesktopHostKeyboardPolicy.ShouldShowContextMenu(e.Key, Keyboard.Modifiers, e.OriginalSource is TextBox))
        {
            if (TryGetFocusedDesktopItem(out var contextButton, out _))
            {
                if (contextButton.ContextMenu is { } contextMenu)
                {
                    contextMenu.PlacementTarget = contextButton;
                    contextMenu.IsOpen = true;
                    e.Handled = true;
                }
            }
            else
            {
                _ = ShowDesktopBackgroundContextMenuAsync();
                e.Handled = true;
            }
        }
    }

    private static bool TryGetNavigationDirection(Key key, out DesktopHostNavigationDirection direction)
    {
        direction = key switch
        {
            Key.Left => DesktopHostNavigationDirection.Left,
            Key.Right => DesktopHostNavigationDirection.Right,
            Key.Up => DesktopHostNavigationDirection.Up,
            Key.Down => DesktopHostNavigationDirection.Down,
            _ => default
        };
        return key is Key.Left or Key.Right or Key.Up or Key.Down;
    }

    private static bool TryGetFocusedDesktopItem(out Button button, out DesktopHostItem item)
    {
        if (Keyboard.FocusedElement is Button { DataContext: DesktopHostItem desktopItem } focusedButton)
        {
            button = focusedButton;
            item = desktopItem;
            return true;
        }
        button = null!;
        item = null!;
        return false;
    }

    private void BeginRename(DesktopHostItem item)
    {
        if (!item.CanRename || !item.IsShellNamespace && !File.Exists(item.FullPath) && !Directory.Exists(item.FullPath)) return;
        item.RenameText = item.Name;
        item.IsRenaming = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!item.IsRenaming) return;
            var editor = FindVisualChildren<TextBox>(DesktopItems)
                .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, item));
            if (editor is null)
            {
                item.IsRenaming = false;
                return;
            }
            editor.Focus();
            Keyboard.Focus(editor);
            editor.Select(0, ExplorerRenamePolicy.GetInitialSelectionLength(item.Name, item.IsDirectory));
        }));
    }

    private async void RenameEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: DesktopHostItem item }) return;
        if (e.Key == Key.Enter)
        {
            await CommitRenameAsync(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.RenameText = item.Name;
            item.IsRenaming = false;
            e.Handled = true;
        }
    }

    private async void RenameEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: DesktopHostItem item }) await CommitRenameAsync(item);
    }

    private async Task CommitRenameAsync(DesktopHostItem item)
    {
        if (!item.IsRenaming) return;
        var originalPath = item.FullPath;
        var newName = item.RenameText;
        if (string.Equals(item.Name, newName, StringComparison.Ordinal))
        {
            item.IsRenaming = false;
            return;
        }

        try
        {
            item.IsRenaming = false;
            var renamedPath = item.IsShellNamespace
                ? await NativeShellContextMenuService.RenameShellItemAsync(originalPath, newName)
                : ExplorerFileOperationService.Rename(originalPath, newName);
            if (!_layoutStore.RenamePath(originalPath, renamedPath))
                System.Diagnostics.Trace.TraceWarning($"The desktop item '{renamedPath}' was renamed, but its saved icon position could not be migrated.");
            RefreshDesktop();
            if (!_desktopItems.Any(candidate => string.Equals(candidate.FullPath, renamedPath, StringComparison.OrdinalIgnoreCase))) return;
            ApplySelection(new HashSet<string>([renamedPath], StringComparer.OrdinalIgnoreCase), renamedPath);
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                var renamedButton = FindVisualChildren<Button>(DesktopItems)
                    .FirstOrDefault(candidate => candidate.DataContext is DesktopHostItem desktopItem &&
                        string.Equals(desktopItem.FullPath, renamedPath, StringComparison.OrdinalIgnoreCase));
                renamedButton?.Focus();
                if (renamedButton is not null) Keyboard.Focus(renamedButton);
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            item.IsRenaming = false;
            item.RenameText = item.Name;
            MessageBox.Show(this, ex.Message, "Could not rename desktop item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnRenameItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DesktopHostItem { CanRename: true } item }) BeginRename(item);
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DesktopHostItem entry }) return;
        OpenDesktopItem(entry);
        e.Handled = true;
    }

    private void OnOpenItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DesktopHostItem entry })
            OpenSelectedDesktopItems(entry);
    }

    private async void OpenSelectedDesktopItems(DesktopHostItem? fallback = null)
    {
        var selection = DesktopHostOpenPolicy.SelectItems(_desktopItems, fallback).ToArray();
        if (selection.Length > 1 && selection.All(entry => !entry.IsDirectory && entry.CanShowNativeContextMenu))
        {
            try
            {
                var owner = new WindowInteropHelper(this).Handle;
                if (await NativeShellContextMenuService.OpenShellItemsAsync(owner, selection.Select(entry => entry.FullPath)))
                    return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                Trace.TraceWarning($"Could not open the selected desktop Shell items together: {ex.Message}");
            }
        }

        foreach (var entry in selection) OpenDesktopItem(entry);
    }

    private void OpenDesktopItem(DesktopHostItem entry)
    {
        try
        {
            if (_routeFoldersToCompanionExplorer && !entry.IsShellNamespace && Directory.Exists(entry.FullPath))
            {
                if (OpenFolderInCompanionExplorer(entry.FullPath)) return;
            }
            if (_routeFoldersToCompanionExplorer && entry.IsShellNamespace &&
                DesktopShellNamespaceCatalog.IsShellNamespaceLocation(entry.FullPath))
            {
                if (MainWindow.TryOpenShellLocationInExistingInstance(entry.FullPath)) return;
            }

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
        var selectedItems = _desktopItems.Where(item => item.IsSelected && item.CanShowNativeContextMenu).ToArray();
        if (selectedItems.Length == 0) selectedItems = [entry];
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.ShowForShellItemsAsync(owner, selectedItems.Select(item => item.FullPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open Windows' context menu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DeleteSelectedDesktopItemsAsync(bool shiftPressed)
    {
        var selection = _desktopItems.Where(item => item.IsSelected && item.CanShowNativeContextMenu).ToArray();
        if (selection.Length == 0) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.DeleteShellItemsAsync(owner, selection.Select(item => item.FullPath), shiftPressed);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not delete desktop items", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshDesktop();
        }
    }

    private async Task ShowSelectedDesktopPropertiesAsync()
    {
        var selection = _desktopItems.Where(item => item.IsSelected && item.CanShowNativeContextMenu).ToArray();
        if (selection.Length == 0) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.ShowPropertiesForShellItemsAsync(owner, selection.Select(item => item.FullPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open desktop item properties", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CopySelectedDesktopItemsAsync(bool cut)
    {
        var selection = _desktopItems.Where(item => item.IsSelected && item.CanShowNativeContextMenu).ToArray();
        if (selection.Length == 0) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            var clipboardSequence = await NativeShellContextMenuService.CopyShellItemsToClipboardAsync(
                owner, selection.Select(item => item.FullPath), cut);
            if (cut) SetCutState(selection, clipboardSequence);
            else ClearCutState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, cut ? "Could not cut desktop items" : "Could not copy desktop items", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task PasteDesktopItemsAsync()
    {
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.PasteIntoShellFolderAsync(owner, "shell:Desktop");
            ClearCutState();
            QueueDesktopRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not paste onto the desktop", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetCutState(IEnumerable<DesktopHostItem> selectedItems, uint clipboardSequence)
    {
        if (!_clipboardListenerRegistered || NativeShellContextMenuService.ReadClipboardSequenceNumber() != clipboardSequence)
        {
            ClearCutState();
            return;
        }

        _cutParsingNames.Clear();
        foreach (var item in selectedItems) _cutParsingNames.Add(item.FullPath);
        _cutClipboardSequence = clipboardSequence;
        ApplyCutState(_desktopItems);
    }

    private void ClearCutState()
    {
        _cutParsingNames.Clear();
        _cutClipboardSequence = 0;
        ApplyCutState(_desktopItems);
    }

    private void ApplyCutState(IEnumerable<DesktopHostItem> items)
    {
        if (_cutParsingNames.Count > 0 && NativeShellContextMenuService.ReadClipboardSequenceNumber() != _cutClipboardSequence)
        {
            _cutParsingNames.Clear();
            _cutClipboardSequence = 0;
        }
        foreach (var item in items) item.IsCut = _cutParsingNames.Contains(item.FullPath);
    }

    private void RefreshCutStateFromClipboard()
    {
        var clipboardSequence = NativeShellContextMenuService.ReadClipboardSequenceNumber();
        if (_cutParsingNames.Count > 0 && clipboardSequence == _cutClipboardSequence)
        {
            ApplyCutState(_desktopItems);
            return;
        }

        var parsingNames = NativeShellContextMenuService.ReadCutItemParsingNamesFromClipboard();
        if (parsingNames.Count == 0)
        {
            ClearCutState();
            return;
        }

        _cutParsingNames.Clear();
        foreach (var parsingName in parsingNames)
        {
            try
            {
                _cutParsingNames.Add(Path.IsPathFullyQualified(parsingName) ? Path.GetFullPath(parsingName) : parsingName);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                _cutParsingNames.Add(parsingName);
            }
        }
        _cutClipboardSequence = clipboardSequence;
        ApplyCutState(_desktopItems);
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == MessageClipboardUpdate) RefreshCutStateFromClipboard();
        return nint.Zero;
    }

    private void OnItemContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { PlacementTarget: Button { DataContext: DesktopHostItem item } } contextMenu) return;
        var selection = DesktopHostSelectionPolicy.PreserveSelectionForContextMenu(_desktopItems, item.FullPath);
        ApplySelection(selection.Paths, selection.AnchorPath);
        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "OpenDesktopItemMenuItem") is { } openItem)
            openItem.IsEnabled = DesktopHostOpenPolicy.SelectItems(_desktopItems).Count > 0;
        var selectedItems = _desktopItems.Where(candidate => candidate.IsSelected).ToArray();
        var selected = selectedItems.Length == 1 ? selectedItems[0] : null;
        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "OpenWithDesktopItemMenuItem") is { } openWithItem)
        {
            openWithItem.Visibility = selected is not null && ShellOpenWithPolicy.CanOpenWith(true, selected.IsDirectory, selected.CanShowNativeContextMenu)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "PrintDesktopItemMenuItem") is { } printItem)
        {
            printItem.Visibility = selected is not null && ShellOpenWithPolicy.CanOpenWith(true, selected.IsDirectory, selected.CanShowNativeContextMenu)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        var nativeCommandEnabled = DesktopHostContextMenuPolicy.CanInvokeNativeCommand(_desktopItems);
        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "ShareDesktopItemMenuItem") is { } shareItem)
        {
            shareItem.Visibility = nativeCommandEnabled ? Visibility.Visible : Visibility.Collapsed;
            shareItem.IsEnabled = nativeCommandEnabled;
        }
        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "CutDesktopItemMenuItem") is { } cutItem)
            cutItem.IsEnabled = nativeCommandEnabled;
        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "CopyDesktopItemMenuItem") is { } copyItem)
            copyItem.IsEnabled = nativeCommandEnabled;
        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "CopyPathDesktopItemMenuItem") is { } copyPathItem)
            copyPathItem.IsEnabled = nativeCommandEnabled;
        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "DeleteDesktopItemMenuItem") is { } deleteItem)
            deleteItem.IsEnabled = nativeCommandEnabled;
        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "PropertiesDesktopItemMenuItem") is { } propertiesItem)
            propertiesItem.IsEnabled = nativeCommandEnabled;
        var singleFilesystemItem = selected is not null
            && !selected.IsShellNamespace
            && TaskbarPinCatalog.IsSupportedTarget(selected.FullPath, selected.IsDirectory);
        var singleStartPinTarget = singleFilesystemItem || (selected is not null && TaskbarPinCatalog.IsSupportedShellNamespaceTarget(selected.FullPath));
        var singleTaskbarPinTarget = selected is not null &&
            (singleFilesystemItem || TaskbarPinCatalog.IsSupportedShellNamespaceTarget(selected.FullPath));
        var pinStartItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "PinStartDesktopItemMenuItem");
        var pinTaskbarItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(menuItem => menuItem.Name == "PinTaskbarDesktopItemMenuItem");
        if (pinStartItem is not null)
        {
            pinStartItem.Visibility = singleStartPinTarget && _pinStartItem is not null ? Visibility.Visible : Visibility.Collapsed;
            pinStartItem.IsEnabled = singleStartPinTarget && _pinStartItem is not null && _isStartItemPinned?.Invoke(selected!.FullPath) != true;
        }
        if (pinTaskbarItem is not null)
        {
            pinTaskbarItem.Visibility = singleTaskbarPinTarget && _pinTaskbarItem is not null ? Visibility.Visible : Visibility.Collapsed;
            pinTaskbarItem.IsEnabled = singleTaskbarPinTarget && _pinTaskbarItem is not null && _isTaskbarItemPinned?.Invoke(selected!.FullPath) != true;
        }
    }

    private void OnPinStartItemClick(object sender, RoutedEventArgs e)
    {
        var item = _desktopItems.SingleOrDefault(candidate => candidate.IsSelected);
        if (item is not null && _pinStartItem?.Invoke(item.FullPath) == true)
            item.IsSelected = true;
    }

    private void OnPinTaskbarItemClick(object sender, RoutedEventArgs e)
    {
        var item = _desktopItems.SingleOrDefault(candidate => candidate.IsSelected);
        if (item is not null && _pinTaskbarItem?.Invoke(item.FullPath) == true)
            item.IsSelected = true;
    }

    private async void OnCutDesktopItemsClick(object sender, RoutedEventArgs e) => await CopySelectedDesktopItemsAsync(cut: true);

    private async void OnOpenWithItemClick(object sender, RoutedEventArgs e)
    {
        var item = _desktopItems.SingleOrDefault(candidate => candidate.IsSelected);
        if (item is null || !ShellOpenWithPolicy.CanOpenWith(true, item.IsDirectory, item.CanShowNativeContextMenu)) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.OpenWithShellItemAsync(owner, item.FullPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open the Open with dialog", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnPrintItemClick(object sender, RoutedEventArgs e)
    {
        var item = _desktopItems.SingleOrDefault(candidate => candidate.IsSelected);
        if (item is null || !ShellOpenWithPolicy.CanOpenWith(true, item.IsDirectory, item.CanShowNativeContextMenu)) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.PrintShellItemAsync(owner, item.FullPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not print the desktop item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnCopyDesktopItemsClick(object sender, RoutedEventArgs e) => await CopySelectedDesktopItemsAsync(cut: false);

    private async void OnShareItemClick(object sender, RoutedEventArgs e)
    {
        var selection = _desktopItems.Where(item => item.IsSelected && item.CanShowNativeContextMenu).ToArray();
        if (selection.Length == 0) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.ShareShellItemsAsync(owner, selection.Select(item => item.FullPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not share the desktop item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnCopyDesktopItemsAsPathClick(object sender, RoutedEventArgs e)
    {
        var selection = _desktopItems.Where(item => item.IsSelected && item.CanShowNativeContextMenu).ToArray();
        if (selection.Length == 0) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.CopyShellItemsAsPathAsync(owner, selection.Select(item => item.FullPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not copy the desktop path", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnDeleteDesktopItemsClick(object sender, RoutedEventArgs e) => await DeleteSelectedDesktopItemsAsync(shiftPressed: false);

    private async void OnShowDesktopItemPropertiesClick(object sender, RoutedEventArgs e) => await ShowSelectedDesktopPropertiesAsync();

    private async void OnPasteDesktopItemsClick(object sender, RoutedEventArgs e) => await PasteDesktopItemsAsync();

    private void OnItemRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DesktopHostItem item }) return;
        _dragCandidate = null;
        var selection = DesktopHostSelectionPolicy.PreserveSelectionForContextMenu(_desktopItems, item.FullPath);
        ApplySelection(selection.Paths, selection.AnchorPath);
        ((Button)sender).Focus();
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

    private void OnDesktopMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInsideDesktopItem(e.OriginalSource as DependencyObject)) return;
        _dragCandidate = null;
        _isMarqueeSelecting = true;
        _marqueeStart = e.GetPosition(this);
        _marqueeTogglesSelection = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _marqueeSelectionBefore = _desktopItems.Where(item => item.IsSelected).Select(item => item.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _marqueeAnchorBefore = _selectionAnchorPath;
        _marqueeInitialSelection = _marqueeTogglesSelection
            ? new HashSet<string>(_marqueeSelectionBefore, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!_marqueeTogglesSelection) ApplySelection(_marqueeInitialSelection, null);
        SelectionMarquee.Visibility = Visibility.Visible;
        UpdateDesktopMarquee(_marqueeStart);
        if (!Mouse.Capture(DesktopSurface, CaptureMode.SubTree)) FinishDesktopMarquee();
    }

    private void OnDesktopMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMarqueeSelecting) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishDesktopMarquee();
            return;
        }
        UpdateDesktopMarquee(e.GetPosition(this));
    }

    private void OnDesktopMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _isMarqueeSelecting)
        {
            UpdateDesktopMarquee(e.GetPosition(this));
            FinishDesktopMarquee();
        }
    }

    private void OnDesktopLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isMarqueeSelecting) FinishDesktopMarquee();
    }

    private void UpdateDesktopMarquee(Point current)
    {
        var rectangle = DesktopHostMarqueePolicy.CreateRectangle(_marqueeStart, current);
        Canvas.SetLeft(SelectionMarquee, rectangle.Left);
        Canvas.SetTop(SelectionMarquee, rectangle.Top);
        SelectionMarquee.Width = rectangle.Width;
        SelectionMarquee.Height = rectangle.Height;
        var items = GetDesktopMarqueeItems();
        var selection = DesktopHostMarqueePolicy.ResolveSelection(items, rectangle, _marqueeInitialSelection, _marqueeTogglesSelection);
        ApplySelection(selection, _selectionAnchorPath);
    }

    private void FinishDesktopMarquee()
    {
        if (!_isMarqueeSelecting) return;
        _isMarqueeSelecting = false;
        SelectionMarquee.Visibility = Visibility.Collapsed;
        var selected = _desktopItems.Where(item => item.IsSelected).Select(item => item.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_selectionAnchorPath is null || !selected.Contains(_selectionAnchorPath))
            _selectionAnchorPath = selected.LastOrDefault();
        if (Mouse.Captured == DesktopSurface) Mouse.Capture(null);
    }

    private void CancelDesktopMarquee()
    {
        if (!_isMarqueeSelecting) return;
        _isMarqueeSelecting = false;
        SelectionMarquee.Visibility = Visibility.Collapsed;
        ApplySelection(_marqueeSelectionBefore, _marqueeAnchorBefore);
        if (Mouse.Captured == DesktopSurface) Mouse.Capture(null);
    }

    private IReadOnlyList<DesktopHostMarqueeItem> GetDesktopMarqueeItems()
    {
        var result = new List<DesktopHostMarqueeItem>(_desktopItems.Count);
        foreach (var item in _desktopItems)
        {
            if (DesktopItems.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject container) continue;
            var button = FindVisualChildren<Button>(container).FirstOrDefault();
            if (button is null || button.ActualWidth <= 0 || button.ActualHeight <= 0) continue;
            try
            {
                var origin = button.TransformToAncestor(this).Transform(new Point(0, 0));
                result.Add(new DesktopHostMarqueeItem(item.FullPath,
                    new Rect(origin.X, origin.Y, button.ActualWidth, button.ActualHeight)));
            }
            catch (InvalidOperationException) { }
        }
        return result;
    }

    private static bool IsInsideDesktopItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button) return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is not Visual and not System.Windows.Media.Media3D.Visual3D) yield break;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private async void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is not { } item ||
            (!item.IsShellNamespace && !File.Exists(item.FullPath) && !Directory.Exists(item.FullPath))) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragCandidate = null;
        var dragSelection = DesktopHostDragPolicy.Resolve(_desktopItems, item);
        if (dragSelection.ItemPaths.Count == 0) return;
        var selectedItems = dragSelection.ItemPaths.Select(path => _desktopItems.FirstOrDefault(candidate =>
            string.Equals(candidate.FullPath, path, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (selectedItems.All(candidate => candidate is not null) && selectedItems.Any(candidate => candidate is { IsShellNamespace: true }))
        {
            _activeNativeShellDragPaths = dragSelection.ItemPaths;
            try
            {
                var owner = new WindowInteropHelper(this).Handle;
                await NativeShellContextMenuService.DragShellItemsAsync(owner, dragSelection.ItemPaths);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not drag Shell items", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { _activeNativeShellDragPaths = null; }
            return;
        }

        var data = new DataObject();
        if (dragSelection.FileDropPaths.Count > 0)
            data.SetData(DataFormats.FileDrop, dragSelection.FileDropPaths.ToArray());
        data.SetData(ItemIdentityFormat, dragSelection.ItemPaths.Count == 1
            ? dragSelection.ItemPaths[0]
            : dragSelection.ItemPaths.ToArray());
        var effects = dragSelection.FileDropPaths.Count > 0
            ? DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link
            : DragDropEffects.Move;
        DragDrop.DoDragDrop((DependencyObject)sender, data, effects);
    }

    private void OnItemDragOver(object sender, DragEventArgs e)
    {
        if (_activeNativeShellDragPaths is { Count: 1 } nativePaths &&
            _desktopItems.Any(item => string.Equals(item.FullPath, nativePaths[0], StringComparison.OrdinalIgnoreCase)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
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
        if (_activeNativeShellDragPaths is { Count: 1 } nativePaths)
        {
            ReorderDesktopItem(nativePaths[0], target.FullPath);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
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
        if (_activeNativeShellDragPaths is { Count: > 0 } nativePaths && nativePaths.All(path =>
                _desktopItems.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
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
        if (_activeNativeShellDragPaths is { Count: > 0 } nativePaths)
        {
            MoveDesktopItems(nativePaths, e.GetPosition(DesktopItems));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(ItemIdentityFormat))
        {
            var internalPaths = ReadInternalItemPaths(e.Data);
            e.Effects = MoveDesktopItems(internalPaths, e.GetPosition(DesktopItems))
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        TransferDroppedItems(e);
    }

    private bool MoveDesktopItems(IReadOnlyList<string> itemPaths, Point point)
    {
        var selectedItems = itemPaths.Select(path => _desktopItems.FirstOrDefault(item =>
            string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (selectedItems.Length == 0 || selectedItems.Any(item => item is null)) return false;

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
        return true;
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

    private void OnNewMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu) return;
        menu.Items.Clear();

        var folder = new MenuItem { Header = "Folder" };
        folder.Click += OnNewFolderClick;
        menu.Items.Add(folder);

        foreach (var item in ShellNewItemCatalog.Read())
        {
            var entry = new MenuItem { Header = item.Label, Tag = item };
            entry.Click += OnNewShellItemClick;
            menu.Items.Add(entry);
        }
    }

    private async void OnNewShellItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ShellNewItem item }) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            if (await NativeShellContextMenuService.CreateShellNewItemAsync(owner, "shell:Desktop", item))
                RefreshDesktop();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not create item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnBackgroundContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var preferences = _layoutStore.ReadPreferences();
        AutoArrangeMenuItem.IsChecked = preferences.AutoArrange;
        AlignToGridMenuItem.IsChecked = preferences.AlignToGrid;
        foreach (var item in SortMenuItem.Items.OfType<MenuItem>())
            item.IsChecked = Enum.TryParse<DesktopHostSortMode>(item.Tag as string, out var mode) && mode == preferences.SortMode;
    }

    private void OnAutoArrangeClick(object sender, RoutedEventArgs e)
    {
        var preferences = _layoutStore.ReadPreferences() with { AutoArrange = AutoArrangeMenuItem.IsChecked };
        if (!SaveLayoutPreferences(preferences)) return;
        if (preferences.AutoArrange) ApplyDesktopArrangement(preferences.SortMode);
    }

    private void OnAlignToGridClick(object sender, RoutedEventArgs e)
    {
        var preferences = _layoutStore.ReadPreferences() with { AlignToGrid = AlignToGridMenuItem.IsChecked };
        if (!SaveLayoutPreferences(preferences)) return;
        if (preferences.AlignToGrid) ApplyDesktopGridAlignment();
    }

    private void OnSortByClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } || !Enum.TryParse<DesktopHostSortMode>(value, out var mode)) return;
        foreach (var item in SortMenuItem.Items.OfType<MenuItem>())
            item.IsChecked = ReferenceEquals(item, sender);
        var preferences = _layoutStore.ReadPreferences() with { SortMode = mode };
        if (!SaveLayoutPreferences(preferences)) return;
        ApplyDesktopArrangement(mode);
    }

    private bool SaveLayoutPreferences(DesktopHostLayoutPreferences preferences)
    {
        if (_layoutStore.SavePreferences(preferences)) return true;
        MessageBox.Show(this, "Desktop layout preferences could not be saved.", "Desktop layout", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void ApplyDesktopArrangement(DesktopHostSortMode sortMode)
    {
        DesktopHostArrangementPolicy.Arrange(_desktopItems, GetLayoutViewports(), sortMode);
        SaveDesktopLayout("Icons were arranged for this session, but their positions could not be saved.");
    }

    private void ApplyDesktopGridAlignment()
    {
        DesktopHostArrangementPolicy.AlignToGrid(_desktopItems, GetLayoutViewports());
        SaveDesktopLayout("Icons were aligned for this session, but their positions could not be saved.");
    }

    private IReadOnlyList<DesktopHostMonitorViewport> GetLayoutViewports() => _desktopMonitors.Count > 0
        ? _desktopMonitors
        : [new DesktopHostMonitorViewport("DISPLAY1", 0, 0,
            Math.Max(DesktopIconWidth, DesktopItems.ActualWidth), Math.Max(DesktopIconHeight, DesktopItems.ActualHeight), true)];

    private async void OnNewFolderClick(object sender, RoutedEventArgs e) => await CreateDesktopFolderAsync();

    private async Task CreateDesktopFolderAsync()
    {
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            if (await NativeShellContextMenuService.CreateFolderInShellFolderAsync(owner, "shell:Desktop"))
                RefreshDesktop();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not create folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenDesktopFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_routeFoldersToCompanionExplorer || !OpenFolderInCompanionExplorer(_userDesktop))
                Process.Start(new ProcessStartInfo(_userDesktop) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Could not open Desktop folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool OpenFolderInCompanionExplorer(string folderPath) =>
        MainWindow.TryOpenFolderInExistingInstance(folderPath);

    private async void OnShowDesktopContextMenuClick(object sender, RoutedEventArgs e) => await ShowDesktopBackgroundContextMenuAsync();

    private async Task ShowDesktopBackgroundContextMenuAsync()
    {
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.ShowForDesktopBackgroundAsync(owner);
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

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint window);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint window);
}
