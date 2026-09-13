using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopTuner;

public partial class TaskbarWindow : Window
{
    private const int DwmColorizationColorChangedMessage = 0x0320;
    private const string PinnedAppDragFormat = "DesktopTuner.PinnedTaskbarApp";
    private const string WindowGroupDragFormat = "DesktopTuner.RunningTaskbarGroup";
    private readonly RunningWindowService _windows = new();
    private readonly TaskbarWindowOrder _windowOrder;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _previewOpenTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _previewCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly Action<TaskbarDisplay> _showStartMenu;
    private readonly Func<bool> _isStartMenuVisible;
    private readonly Action<DesktopPreferences> _persistPreferences;
    private readonly Action _closeAllTaskbars;
    private readonly Action _showSettings;
    private readonly Action _quitApplication;
    private DesktopPreferences _preferences = new(TaskbarEdge.Bottom);
    private TaskbarEdge _edge;
    private TaskbarSize _size;
    private bool _autoHide;
    private bool _autoHideWhenMaximized;
    private bool _maximizedWindowOnDisplay;
    private bool _collapsed;
    private bool _nativeReady;
    private bool _nativeTrayExposed;
    private bool _isDark;
    private PinnedTaskbarApp? _pinDragCandidate;
    private Point _pinDragStart;
    private TaskbarWindowGroup? _windowDragCandidate;
    private Point _windowDragStart;
    private TaskbarWindowGroup? _pendingPreviewGroup;
    private Button? _pendingPreviewTarget;
    private TaskbarPreviewWindow? _previewWindow;

    public TaskbarDisplay Display { get; private set; }

    public TaskbarWindow(TaskbarDisplay display, Action<TaskbarDisplay> showStartMenu, Func<bool> isStartMenuVisible, DesktopPreferences preferences, TaskbarWindowOrder windowOrder, Action<DesktopPreferences> persistPreferences, Action closeAllTaskbars, Action showSettings, Action quitApplication)
    {
        InitializeComponent();
        _isDark = TaskbarTheme.ReadSystemDarkMode();
        TaskbarTheme.Apply(_isDark);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Display = display;
        _showStartMenu = showStartMenu;
        _isStartMenuVisible = isStartMenuVisible;
        _windowOrder = windowOrder;
        _persistPreferences = persistPreferences;
        _closeAllTaskbars = closeAllTaskbars;
        _showSettings = showSettings;
        _quitApplication = quitApplication;
        _refreshTimer.Tick += (_, _) => RefreshWindows();
        _autoHideTimer.Tick += (_, _) => AutoHideTimer_Tick();
        _previewOpenTimer.Tick += (_, _) => OpenPendingPreview();
        _previewCloseTimer.Tick += (_, _) => ClosePreviewIfPointerOutside();
        SourceInitialized += (_, _) =>
        {
            _nativeReady = true;
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProc);
            ApplyLayout();
            Display = TaskbarDisplayService.ReadWindowDpi(Display, this);
            ApplyLayout();
        };
        SetPreferences(preferences);
    }

    public void SetPreferences(DesktopPreferences preferences)
    {
        _preferences = preferences with { PinnedApps = preferences.PinnedApps ?? [] };
        _edge = _preferences.TaskbarEdge;
        _size = _preferences.TaskbarSize;
        _autoHide = _preferences.AutoHide;
        _autoHideWhenMaximized = _preferences.AutoHideWhenMaximized;
        _collapsed = (_autoHide || _autoHideWhenMaximized && _maximizedWindowOnDisplay) && !_isStartMenuVisible() && !IsMouseOver;
        ApplyLayout();
        if (IsLoaded) RefreshWindows();
        if (IsLoaded) Dispatcher.BeginInvoke(new Action(UpdateButtonCentering));
        if (!_autoHide && !_autoHideWhenMaximized) _autoHideTimer.Stop();
        else if (IsLoaded) _autoHideTimer.Start();
    }

    private void ApplyLayout()
    {
        var layoutPreferences = _preferences with { TaskbarEdge = _edge, TaskbarSize = _size, AutoHide = _autoHide };
        RootBorder.Background = TaskbarTheme.CreateBackground(_isDark, _preferences.TaskbarTransparency);
        RootBorder.BorderBrush = TaskbarTheme.GetBrush("TaskbarBorderBrush");
        var bounds = TaskbarLayoutCalculator.Calculate(Display, layoutPreferences, _collapsed);
        var trayBounds = NativeTaskbarTrayService.FindTrayBounds(Display);
        var integratedBounds = TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(Display, layoutPreferences, trayBounds, _collapsed);
        _nativeTrayExposed = integratedBounds is not null;
        if (integratedBounds is { } trayIntegratedBounds) bounds = trayIntegratedBounds;
        SettingsButton.Visibility = _nativeTrayExposed ? Visibility.Collapsed : Visibility.Visible;
        TrayButton.Visibility = _nativeTrayExposed ? Visibility.Collapsed : Visibility.Visible;
        VolumeButton.Visibility = _nativeTrayExposed ? Visibility.Collapsed : Visibility.Visible;
        ClockButton.Visibility = _nativeTrayExposed ? Visibility.Collapsed : Visibility.Visible;
        CloseBarButton.Width = _nativeTrayExposed ? 32 : double.NaN;
        CloseBarButton.Height = _nativeTrayExposed ? 32 : double.NaN;
        CloseBarButton.Padding = _nativeTrayExposed ? new Thickness(0) : new Thickness(12, 7, 12, 7);
        CloseBarButton.Margin = _nativeTrayExposed ? new Thickness(0) : new Thickness(2, 0, 2, 0);
        RootBorder.Padding = _nativeTrayExposed && _edge == TaskbarEdge.Bottom
            ? new Thickness(10, 4, 0, 4)
            : new Thickness(10, 4, 10, 4);
        var vertical = _edge is TaskbarEdge.Left or TaskbarEdge.Right;
        Width = bounds.Width / Display.ScaleX;
        Height = bounds.Height / Display.ScaleY;
        if (_nativeReady && !TaskbarDisplayService.PositionWindow(this, bounds))
            Trace.TraceError($"Could not place taskbar on display {Display.DeviceName}.");
        LayoutGrid.ColumnDefinitions.Clear();
        LayoutGrid.RowDefinitions.Clear();
        if (vertical)
        {
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(StartSegment, 0);
            Grid.SetColumn(StartSegment, 0);
            Grid.SetRow(AppsSegment, 1);
            Grid.SetColumn(AppsSegment, 0);
            Grid.SetRow(SystemSegment, 2);
            Grid.SetColumn(SystemSegment, 0);
            RightControls.Orientation = Orientation.Vertical;
            TaskButtonsStack.Orientation = Orientation.Vertical;
            CenterSpacer.Visibility = Visibility.Collapsed;
            PinnedItems.ItemsPanel = (ItemsPanelTemplate)FindResource("VerticalWindowPanel");
            WindowItems.ItemsPanel = (ItemsPanelTemplate)FindResource("VerticalWindowPanel");
            WindowScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            WindowScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            WindowScroller.Margin = new Thickness(0, 10, 0, 10);
            RootBorder.Padding = new Thickness(5, 10, 5, 10);
            RootBorder.BorderThickness = _edge == TaskbarEdge.Left ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
            PinDivider.Width = 24;
            PinDivider.Height = 1;
            PinDivider.Margin = new Thickness(0, 5, 0, 5);
        }
        else
        {
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(StartSegment, 0);
            Grid.SetColumn(StartSegment, 0);
            Grid.SetRow(AppsSegment, 0);
            Grid.SetColumn(AppsSegment, 1);
            Grid.SetRow(SystemSegment, 0);
            Grid.SetColumn(SystemSegment, 2);
            RightControls.Orientation = Orientation.Horizontal;
            TaskButtonsStack.Orientation = Orientation.Horizontal;
            CenterSpacer.Visibility = Visibility.Visible;
            PinnedItems.ItemsPanel = (ItemsPanelTemplate)FindResource("HorizontalWindowPanel");
            WindowItems.ItemsPanel = (ItemsPanelTemplate)FindResource("HorizontalWindowPanel");
            WindowScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            WindowScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            WindowScroller.Margin = new Thickness(10, 0, 10, 0);
            RootBorder.Padding = new Thickness(10, 4, _nativeTrayExposed ? 0 : 10, 4);
            RootBorder.BorderThickness = _edge == TaskbarEdge.Top ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
            PinDivider.Width = 1;
            PinDivider.Height = 24;
            PinDivider.Margin = new Thickness(5, 0, 5, 0);
        }

        if (_preferences.TaskbarLayout == TaskbarStyle.Floating)
        {
            RootBorder.CornerRadius = new CornerRadius(14);
            RootBorder.Background = TaskbarTheme.CreateBackground(_isDark, _preferences.TaskbarTransparency);
            RootBorder.BorderThickness = new Thickness(1);
            ResetSegments();
        }
        else if (_preferences.TaskbarLayout == TaskbarStyle.Segmented)
        {
            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.Background = Brushes.Transparent;
            RootBorder.BorderBrush = Brushes.Transparent;
            RootBorder.BorderThickness = new Thickness(0);
            StyleSegment(StartSegment, vertical);
            StyleSegment(AppsSegment, vertical);
            StyleSegment(SystemSegment, vertical);
        }
        else
        {
            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.Background = TaskbarTheme.CreateBackground(_isDark, _preferences.TaskbarTransparency);
            RootBorder.BorderBrush = TaskbarTheme.GetBrush("TaskbarBorderBrush");
            RootBorder.BorderThickness = vertical
                ? (_edge == TaskbarEdge.Left ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0))
                : (_edge == TaskbarEdge.Top ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0));
            ResetSegments();
        }
    }

    private void ResetSegments()
    {
        foreach (var segment in new[] { StartSegment, AppsSegment, SystemSegment })
        {
            segment.Background = Brushes.Transparent;
            segment.BorderBrush = Brushes.Transparent;
            segment.BorderThickness = new Thickness(0);
            segment.CornerRadius = new CornerRadius(0);
            segment.Margin = new Thickness(0);
            segment.Padding = new Thickness(0);
        }
    }

    private void StyleSegment(Border segment, bool vertical)
    {
        segment.Background = TaskbarTheme.CreateBackground(_isDark, _preferences.TaskbarTransparency);
        segment.BorderBrush = TaskbarTheme.GetBrush("TaskbarSegmentBorderBrush");
        segment.BorderThickness = new Thickness(1);
        segment.CornerRadius = new CornerRadius(11);
        segment.Margin = vertical ? new Thickness(3, 3, 3, 0) : new Thickness(3, 0, 3, 0);
        segment.Padding = new Thickness(3);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout();
        Display = TaskbarDisplayService.ReadWindowDpi(Display, this);
        RefreshWindows();
        ApplyLayout();
        UpdateClock();
        _refreshTimer.Start();
        if (_autoHide || _autoHideWhenMaximized) _autoHideTimer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.RemoveHook(WindowProc);
        _refreshTimer.Stop();
        _autoHideTimer.Stop();
        _previewOpenTimer.Stop();
        _previewCloseTimer.Stop();
        _previewWindow?.Close();
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == DwmColorizationColorChangedMessage)
        {
            TaskbarTheme.Apply(_isDark);
            ApplyLayout();
        }
        return 0;
    }

    private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        var isDark = TaskbarTheme.ReadSystemDarkMode();
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _isDark = isDark;
            TaskbarTheme.Apply(_isDark);
            ApplyLayout();
        }));
    }

    private void RefreshWindows()
    {
        var windows = _windowOrder.Synchronize(_windows.Enumerate());
        var wasMaximizedWindowOnDisplay = _maximizedWindowOnDisplay;
        _maximizedWindowOnDisplay = TaskbarAutoHidePolicy.HasMaximizedWindowOnDisplay(windows, Display);
        if (wasMaximizedWindowOnDisplay && !_maximizedWindowOnDisplay && !_autoHide && _collapsed)
        {
            _collapsed = false;
            ApplyLayout();
        }
        var vertical = _edge is TaskbarEdge.Left or TaskbarEdge.Right;
        var pinnedApps = _preferences.PinnedApps!;
        PinnedItems.ItemsSource = pinnedApps.Select(app =>
        {
            var appWindows = windows.Where(window => TaskbarWindowGrouping.MatchesPinnedApp(app, window)).ToList();
            return TaskbarButtonViewModel.FromPin(app, _preferences, vertical, appWindows.Count > 0, appWindows.Any(window => window.IsForeground));
        }).ToList();
        WindowItems.ItemsSource = TaskbarWindowGrouping.Create(windows, _preferences.TaskbarGrouping, GetWindowButtonCapacity(), pinnedApps)
            .Select(group => TaskbarButtonViewModel.FromWindowGroup(group, _preferences, vertical)).ToList();
        EmptyText.Visibility = windows.Count == 0 && _preferences.PinnedApps!.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(new Action(UpdateButtonCentering));
        UpdateClock();
    }

    private void WindowScroller_SizeChanged(object sender, SizeChangedEventArgs e) => Dispatcher.BeginInvoke(new Action(UpdateButtonCentering));

    private void UpdateButtonCentering()
    {
        if (!IsLoaded || _edge is TaskbarEdge.Left or TaskbarEdge.Right || _preferences.TaskbarButtonAlignment == TaskbarButtonAlignment.Left)
        {
            CenterSpacer.Width = 0;
            return;
        }

        TaskButtonsStack.UpdateLayout();
        var contentWidth = PinnedItems.DesiredSize.Width + PinDivider.DesiredSize.Width + WindowItems.DesiredSize.Width;
        CenterSpacer.Width = TaskbarButtonAlignmentPolicy.CalculateLeadingSpacer(WindowScroller.ViewportWidth, contentWidth, _preferences.TaskbarButtonAlignment);
    }

    private int GetWindowButtonCapacity()
    {
        var bounds = TaskbarLayoutCalculator.Calculate(Display, _preferences, collapsed: false);
        var vertical = _edge is TaskbarEdge.Left or TaskbarEdge.Right;
        var availableLength = vertical ? bounds.Height / Display.ScaleY : bounds.Width / Display.ScaleX;
        var iconPixels = TaskbarIconSizePolicy.GetPixels(_preferences.TaskbarIconSize);
        var gap = TaskbarButtonSpacingPolicy.GetGap(_preferences.TaskbarButtonSpacing);
        var buttonSpan = vertical
            ? Math.Max(44, iconPixels + 18) + gap * 2
            : (_preferences.TaskbarShowLabels ? 140 : iconPixels + 24) + (gap - 2) * 2;
        var reservedControlsLength = vertical
            ? (_nativeTrayExposed ? 170 : 210)
            : (_nativeTrayExposed ? 250 : 290);
        var reservedLength = reservedControlsLength + (_preferences.PinnedApps?.Count ?? 0) * buttonSpan;
        return Math.Max(1, (int)Math.Floor((availableLength - reservedLength) / buttonSpan));
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("h:mm tt");
        DateText.Text = now.ToString("ddd, MMM d");
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoHideTimer.Stop();
        if (!_collapsed) return;
        _collapsed = false;
        ApplyLayout();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_autoHide || _autoHideWhenMaximized) _autoHideTimer.Start();
        QueuePreviewClose();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var canPin = GetDroppableItems(e.Data).Any();
        e.Effects = canPin ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        RootBorder.BorderBrush = canPin ? TaskbarTheme.GetBrush("TaskbarAccentFallbackBrush") : TaskbarTheme.GetBrush("TaskbarBorderBrush");
        RootBorder.Background = canPin ? TaskbarTheme.GetBrush("TaskbarHoverBrush") : TaskbarTheme.CreateBackground(_isDark, _preferences.TaskbarTransparency);
    }

    private void Window_DragLeave(object sender, DragEventArgs e) => ResetDropHighlight();

    private void Window_Drop(object sender, DragEventArgs e)
    {
        ResetDropHighlight();
        var currentPins = _preferences.PinnedApps ?? [];
        var pins = TaskbarPinCatalog.AddDroppedFiles(currentPins, GetDroppableItems(e.Data), Directory.Exists);
        if (pins.Count == currentPins.Count) return;

        _preferences = _preferences with { PinnedApps = pins };
        _persistPreferences(_preferences);
        RefreshWindows();
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private IEnumerable<string> GetDroppableItems(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] paths)
            return [];

        return paths.Where(path => File.Exists(path) || Directory.Exists(path))
            .Where(path => TaskbarPinCatalog.AddDroppedFiles(_preferences.PinnedApps ?? [], [path], Directory.Exists).Count > (_preferences.PinnedApps?.Count ?? 0));
    }

    private void ResetDropHighlight()
    {
        if (_preferences.TaskbarLayout == TaskbarStyle.Segmented)
        {
            RootBorder.BorderBrush = Brushes.Transparent;
            RootBorder.Background = Brushes.Transparent;
        }
        else
        {
            RootBorder.BorderBrush = TaskbarTheme.GetBrush("TaskbarBorderBrush");
            RootBorder.Background = TaskbarTheme.CreateBackground(_isDark, _preferences.TaskbarTransparency);
        }
    }

    private void PinnedButton_DragOver(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: PinnedTaskbarApp targetApp } button &&
            e.Data.GetDataPresent(PinnedAppDragFormat) && e.Data.GetData(PinnedAppDragFormat) is string sourcePath)
        {
            var canReorder = !string.Equals(sourcePath, targetApp.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            button.Background = canReorder ? TaskbarTheme.GetBrush("TaskbarPressedBrush") : Brushes.Transparent;
            e.Effects = canReorder ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (sender is Button { Tag: PinnedTaskbarApp app } && !app.IsDirectory &&
            File.Exists(app.ExecutablePath) && GetDroppedDocuments(e.Data).Any())
        {
            ((Button)sender).Background = TaskbarTheme.GetBrush("TaskbarPressedBrush");
            e.Effects = DragDropEffects.Link;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void PinnedButton_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button button) button.Background = Brushes.Transparent;
    }

    private void PinnedButton_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Tag is not PinnedTaskbarApp app) return;
        button.Background = Brushes.Transparent;

        if (e.Data.GetDataPresent(PinnedAppDragFormat) && e.Data.GetData(PinnedAppDragFormat) is string sourcePath)
        {
            var currentPins = _preferences.PinnedApps ?? [];
            var pins = TaskbarPinCatalog.Move(currentPins, sourcePath, app.ExecutablePath);
            if (pins.Select(pin => pin.ExecutablePath).SequenceEqual(currentPins.Select(pin => pin.ExecutablePath), StringComparer.OrdinalIgnoreCase)) return;
            _preferences = _preferences with { PinnedApps = pins };
            _persistPreferences(_preferences);
            RefreshWindows();
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (app.IsDirectory || !File.Exists(app.ExecutablePath)) return;
        var documents = GetDroppedDocuments(e.Data).ToArray();
        if (documents.Length == 0) return;
        try
        {
            var startInfo = new ProcessStartInfo(app.ExecutablePath) { UseShellExecute = true };
            foreach (var document in documents) startInfo.ArgumentList.Add(document);
            Process.Start(startInfo);
            e.Effects = DragDropEffects.Link;
            e.Handled = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open dropped items", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void PinnedButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: PinnedTaskbarApp app }) return;
        _pinDragCandidate = app;
        _pinDragStart = e.GetPosition(this);
    }

    private void PinnedButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _pinDragCandidate = null;

    private void PinnedButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pinDragCandidate is null || e.LeftButton != MouseButtonState.Pressed || sender is not Button button) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _pinDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _pinDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var data = new DataObject(PinnedAppDragFormat, _pinDragCandidate.ExecutablePath);
        try { DragDrop.DoDragDrop(button, data, DragDropEffects.Move); }
        finally { _pinDragCandidate = null; }
    }

    private void WindowButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: TaskbarWindowGroup group }) return;
        _windowDragCandidate = group;
        _windowDragStart = e.GetPosition(this);
    }

    private void WindowButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _windowDragCandidate = null;

    private void WindowButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Button { Tag: TaskbarWindowGroup group } button) return;
        _previewCloseTimer.Stop();
        _pendingPreviewGroup = group;
        _pendingPreviewTarget = button;
        _previewOpenTimer.Stop();
        _previewOpenTimer.Start();
    }

    private void WindowButton_MouseLeave(object sender, MouseEventArgs e)
    {
        _previewOpenTimer.Stop();
        _pendingPreviewGroup = null;
        _pendingPreviewTarget = null;
        QueuePreviewClose();
    }

    private void OpenPendingPreview()
    {
        _previewOpenTimer.Stop();
        if (_pendingPreviewGroup is not { } group || _pendingPreviewTarget is not { IsMouseOver: true } target) return;
        if (_previewWindow is { IsVisible: true } existing && existing.Matches(group.Windows)) return;

        ShowWindowPreview(group, target, activate: false);
    }

    private void ShowWindowPreview(TaskbarWindowGroup group, Button target, bool activate)
    {
        if (_previewWindow is { IsVisible: true } existing && existing.Matches(group.Windows))
        {
            return;
        }

        _previewWindow?.Close();
        var preview = new TaskbarPreviewWindow(group.Windows, Display, _edge, target) { ShowActivated = activate };
        _previewWindow = preview;
        preview.Closed += (_, _) =>
        {
            if (ReferenceEquals(_previewWindow, preview)) _previewWindow = null;
        };
        preview.MouseEnter += (_, _) => _previewCloseTimer.Stop();
        preview.MouseLeave += (_, _) => QueuePreviewClose();
        preview.Show();
        if (activate) preview.Activate();
    }

    private void QueuePreviewClose()
    {
        if (_previewWindow is not null)
        {
            _previewCloseTimer.Stop();
            _previewCloseTimer.Start();
        }
    }

    private void ClosePreviewIfPointerOutside()
    {
        _previewCloseTimer.Stop();
        if (_previewWindow?.IsMouseOver != true) _previewWindow?.Close();
    }

    private void WindowButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_windowDragCandidate is null || e.LeftButton != MouseButtonState.Pressed || sender is not Button button) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _windowDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _windowDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var data = new DataObject(WindowGroupDragFormat, _windowDragCandidate);
        try
        {
            e.Handled = true;
            DragDrop.DoDragDrop(button, data, DragDropEffects.Move);
        }
        finally
        {
            _windowDragCandidate = null;
            button.Background = Brushes.Transparent;
        }
    }

    private void WindowButton_DragOver(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: TaskbarWindowGroup target } button &&
            e.Data.GetDataPresent(WindowGroupDragFormat) && e.Data.GetData(WindowGroupDragFormat) is TaskbarWindowGroup source &&
            !source.Windows.Select(window => window.Handle).Intersect(target.Windows.Select(window => window.Handle)).Any())
        {
            button.Background = TaskbarTheme.GetBrush("TaskbarPressedBrush");
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = false;
    }

    private void WindowButton_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button button) button.Background = Brushes.Transparent;
    }

    private void WindowButton_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button { Tag: TaskbarWindowGroup target } ||
            !e.Data.GetDataPresent(WindowGroupDragFormat) || e.Data.GetData(WindowGroupDragFormat) is not TaskbarWindowGroup moving) return;

        if (_windowOrder.MoveGroup(_windows.Enumerate(), moving, target)) RefreshWindows();
        e.Handled = true;
    }

    private static IEnumerable<string> GetDroppedDocuments(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] paths) return [];
        return paths.Where(File.Exists).Where(path => !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase));
    }

    private void AutoHideTimer_Tick()
    {
        if (!TaskbarAutoHidePolicy.ShouldCollapse(_autoHide, _autoHideWhenMaximized, _maximizedWindowOnDisplay, IsMouseOver, _isStartMenuVisible())) return;
        _collapsed = true;
        ApplyLayout();
        _autoHideTimer.Stop();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => _showStartMenu(Display);

    private void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TaskbarWindowGroup group } button) return;
        if (group.Windows.Count > 1)
        {
            _previewOpenTimer.Stop();
            _pendingPreviewGroup = null;
            _pendingPreviewTarget = null;
            _previewCloseTimer.Stop();
            ShowWindowPreview(group, button, activate: true);
            return;
        }

        _previewWindow?.Close();
        RunningWindowService.ActivateOrMinimize(group.Windows[0]);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.Tag is TaskbarWindowGroup group)
        {
            foreach (var window in group.Windows) RunningWindowService.Minimize(window);
        }
        else if (item.Tag is RunningWindow window) RunningWindowService.Minimize(window);
    }

    private void CloseWindows_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.Tag is TaskbarWindowGroup group)
        {
            foreach (var window in group.Windows) RunningWindowService.Close(window);
        }
        else if (item.Tag is RunningWindow window) RunningWindowService.Close(window);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var currentPins = _preferences.PinnedApps ?? [];
        if (sender is not MenuItem item) return;
        var window = item.Tag switch
        {
            RunningWindow runningWindow => runningWindow,
            TaskbarWindowGroup group => group.Windows.FirstOrDefault(),
            _ => null
        };
        if (window is null) return;
        var pins = TaskbarPinCatalog.Add(currentPins, window.ApplicationName, window.ExecutablePath);
        if (pins.Count == currentPins.Count) return;
        _preferences = _preferences with { PinnedApps = pins };
        _persistPreferences(_preferences);
        RefreshWindows();
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: PinnedTaskbarApp app }) return;
        var pins = TaskbarPinCatalog.Remove(_preferences.PinnedApps ?? [], app.ExecutablePath);
        _preferences = _preferences with { PinnedApps = pins };
        _persistPreferences(_preferences);
        RefreshWindows();
    }

    private void PinnedButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PinnedTaskbarApp app }) return;
        var openWindows = _windows.Enumerate()
            .Where(window => TaskbarWindowGrouping.MatchesPinnedApp(app, window))
            .ToList();
        if (openWindows.Count > 1)
        {
            ShowWindowPreview(new TaskbarWindowGroup(app.Name, app.Name, openWindows), (Button)sender, activate: true);
            return;
        }
        if (openWindows.Count == 1)
        {
            RunningWindowService.ActivateOrMinimize(openWindows[0]);
            return;
        }
        var isDirectory = app.IsDirectory || Directory.Exists(app.ExecutablePath);
        if (!isDirectory && !File.Exists(app.ExecutablePath))
        {
            MessageBox.Show(this, $"The pinned app could not be found:\n{app.ExecutablePath}", "Pinned app unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var startInfo = isDirectory
                ? new ProcessStartInfo("explorer.exe") { UseShellExecute = true }
                : new ProcessStartInfo(app.ExecutablePath) { UseShellExecute = true };
            if (isDirectory) startInfo.ArgumentList.Add(app.ExecutablePath);
            Process.Start(startInfo);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not launch pinned app", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open Settings", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SoundSettings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open Sound settings", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void AudioOutputContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Items.Clear();
        try
        {
            var outputs = AudioEndpointVolumeService.EnumerateOutputs();
            if (outputs.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No active output devices", IsEnabled = false });
            else
            {
                foreach (var output in outputs)
                {
                    var item = new MenuItem
                    {
                        Header = output.Name,
                        Tag = output,
                        IsCheckable = true,
                        IsChecked = output.IsDefault,
                        IsEnabled = !output.IsDefault,
                        ToolTip = output.IsDefault ? "Current default output" : null
                    };
                    item.Click += AudioOutputDevice_Click;
                    menu.Items.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            menu.Items.Add(new MenuItem { Header = "Could not list output devices", IsEnabled = false, ToolTip = ex.Message });
            Trace.TraceWarning($"Could not enumerate audio output devices: {ex}");
        }

        menu.Items.Add(new Separator());
        var settingsItem = new MenuItem { Header = "Sound settings…" };
        settingsItem.Click += SoundSettings_Click;
        menu.Items.Add(settingsItem);
    }

    private void AudioOutputDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AudioOutputDevice output }) return;
        try
        {
            AudioEndpointVolumeService.SetDefaultOutput(output.Id);
            var state = AudioEndpointVolumeService.ReadDefaultOutput();
            VolumeButton.Content = state.Muted ? "🔇" : state.Volume < 0.34f ? "🔈" : "🔊";
            VolumeButton.ToolTip = AudioVolumePolicy.GetLabel(state.Volume, state.Muted) + " · Click for Sound settings · Right-click to choose output";
        }
        catch (Exception ex)
        {
            VolumeButton.ToolTip = $"Could not select audio output: {ex.Message} · Open Sound settings";
            Trace.TraceWarning($"Could not set the default audio output to '{output.Name}': {ex}");
            SoundSettings_Click(this, new RoutedEventArgs());
        }
    }

    private void VolumeButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        try
        {
            var muted = AudioEndpointVolumeService.ToggleDefaultOutputMute();
            VolumeButton.Content = muted ? "🔇" : "🔊";
            var state = AudioEndpointVolumeService.ReadDefaultOutput();
            VolumeButton.ToolTip = AudioVolumePolicy.GetLabel(state.Volume, muted) + " · Click for Sound settings · Right-click to choose output";
        }
        catch (Exception ex)
        {
            VolumeButton.ToolTip = $"Could not change audio mute: {ex.Message}";
            Trace.TraceWarning($"Could not toggle default audio output mute: {ex}");
        }
        e.Handled = true;
    }

    private void VolumeButton_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            var state = AudioEndpointVolumeService.ReadDefaultOutput();
            var level = AudioVolumePolicy.Adjust(state.Volume, e.Delta);
            AudioEndpointVolumeService.SetDefaultOutputVolume(level);
            VolumeButton.Content = state.Muted ? "🔇" : level < 0.34f ? "🔈" : "🔊";
            VolumeButton.ToolTip = AudioVolumePolicy.GetLabel(level, state.Muted) + " · Middle-click to mute · Click for Sound settings · Right-click to choose output";
        }
        catch (Exception ex)
        {
            VolumeButton.ToolTip = $"Could not adjust audio volume: {ex.Message}";
            Trace.TraceWarning($"Could not adjust default audio output volume: {ex}");
        }
        e.Handled = true;
    }

    private void Clock_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenNotificationCenter();

    private void Tray_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.FocusNotificationArea();

    private void Widgets_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenWidgets();

    private void TaskbarContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        AutoHideMenuItem.IsChecked = _autoHide;
        AutoHideWhenMaximizedMenuItem.IsChecked = _autoHideWhenMaximized;
    }

    private void ShowSettings_Click(object sender, RoutedEventArgs e) => _showSettings();

    private void Quit_Click(object sender, RoutedEventArgs e) => _quitApplication();

    private void AutoHideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var preferences = _preferences with { AutoHide = AutoHideMenuItem.IsChecked };
        _persistPreferences(preferences);
    }

    private void AutoHideWhenMaximizedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var preferences = _preferences with { AutoHideWhenMaximized = AutoHideWhenMaximizedMenuItem.IsChecked };
        _persistPreferences(preferences);
    }

    private void CloseBar_Click(object sender, RoutedEventArgs e) => _closeAllTaskbars();
}
