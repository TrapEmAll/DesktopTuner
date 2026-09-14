using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
    private readonly NativeTaskbarAppBarService _nativeAppBar = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _batteryRefreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _microphoneRefreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };
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
    private bool _replacementWorkAreaEnabled;
    private bool? _systemBackdropForCurrentStyle;
    private bool _nativeTrayExposed;
    private bool _isDark;
    private bool _keyboardFocusActive;
    private nint _previousForegroundWindow;
    private TaskbarBatteryStatus? _batteryStatus;
    private (float Volume, bool Muted)? _microphoneStatus;
    private bool _hasMicrophoneDevice;
    private bool _reportedMicrophoneEnumerationFailure;
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
        _batteryRefreshTimer.Tick += (_, _) => UpdateBatteryStatus();
        _microphoneRefreshTimer.Tick += (_, _) => UpdateMicrophoneStatus();
        _autoHideTimer.Tick += (_, _) => AutoHideTimer_Tick();
        _previewOpenTimer.Tick += (_, _) => OpenPendingPreview();
        _previewCloseTimer.Tick += (_, _) => ClosePreviewIfPointerOutside();
        SourceInitialized += (_, _) =>
        {
            _nativeReady = true;
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
            ApplyLayout();
            Display = TaskbarDisplayService.ReadWindowDpi(Display, this);
            ApplyLayout();
        };
        SetPreferences(preferences);
    }

    public void SetPreferences(DesktopPreferences preferences)
    {
        _preferences = preferences with { PinnedApps = preferences.PinnedApps ?? [], TaskbarSystemButtons = TaskbarSystemButtonVisibility.Normalize(preferences.TaskbarSystemButtons) };
        _edge = _preferences.TaskbarEdge;
        _size = _preferences.TaskbarSize;
        _autoHide = _preferences.AutoHide;
        _autoHideWhenMaximized = _preferences.AutoHideWhenMaximized;
        _collapsed = (_autoHide || _autoHideWhenMaximized && _maximizedWindowOnDisplay) && !_isStartMenuVisible() && !IsMouseOver;
        ApplyLayout();
        if (IsLoaded) RefreshWindows();
        if (IsLoaded) UpdateBatteryStatus();
        if (IsLoaded) UpdateMicrophoneStatus();
        if (IsLoaded) Dispatcher.BeginInvoke(new Action(UpdateButtonCentering));
        if (!_autoHide && !_autoHideWhenMaximized) _autoHideTimer.Stop();
        else if (IsLoaded) _autoHideTimer.Start();
    }

    public void UpdateDisplay(TaskbarDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (!string.Equals(Display.DeviceName, display.DeviceName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A taskbar window can only be reassigned to the same display.", nameof(display));
        Display = _nativeReady ? TaskbarDisplayService.ReadWindowDpi(display, this) : display;
        ApplyLayout();
        if (IsLoaded) RefreshWindows();
    }

    public void EnableReplacementWorkArea(bool enabled)
    {
        if (_replacementWorkAreaEnabled == enabled
            && (!enabled || !_preferences.ReplaceNativeTaskbar
                || !TaskbarAppBarPolicy.ShouldRegister(_preferences.TaskbarLayout)
                || _nativeAppBar.IsRegistered))
            return;
        _replacementWorkAreaEnabled = enabled;
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        UpdateSystemBackdrop();
        var layoutPreferences = _preferences with { TaskbarEdge = _edge, TaskbarSize = _size, AutoHide = _autoHide };
        RootBorder.Background = TaskbarTheme.CreateBackground(_isDark, GetEffectiveTransparency());
        RootBorder.BorderBrush = TaskbarTheme.GetBrush("TaskbarBorderBrush");
        var bounds = TaskbarLayoutCalculator.Calculate(Display, layoutPreferences, _collapsed);
        var registerAppBar = _replacementWorkAreaEnabled && _preferences.ReplaceNativeTaskbar
            && TaskbarAppBarPolicy.ShouldRegister(_preferences.TaskbarLayout);
        if (registerAppBar && _nativeReady && !_nativeAppBar.IsRegistered)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (!_nativeAppBar.Register(handle, Display, _edge))
                Trace.TraceWarning($"Taskbar on {Display.DeviceName} is running without a reserved Windows work area.");
        }
        else if (!registerAppBar && _nativeAppBar.IsRegistered)
        {
            _nativeAppBar.Unregister();
        }
        if (_nativeAppBar.IsRegistered && _nativeAppBar.UpdatePosition(bounds) is { } appBarBounds)
            bounds = appBarBounds;
        var trayBounds = _preferences.ReplaceNativeTaskbar ? null : NativeTaskbarTrayService.FindTrayBounds(Display);
        var integratedBounds = TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(Display, layoutPreferences, trayBounds, _collapsed);
        _nativeTrayExposed = integratedBounds is not null;
        var systemButtons = _preferences.TaskbarSystemButtons!;
        BatteryButton.Visibility = _preferences.ReplaceNativeTaskbar && systemButtons.Battery && _batteryStatus is not null ? Visibility.Visible : Visibility.Collapsed;
        QuickSettingsButton.Visibility = !_nativeTrayExposed && systemButtons.QuickSettings ? Visibility.Visible : Visibility.Collapsed;
        if (integratedBounds is { } trayIntegratedBounds) bounds = trayIntegratedBounds;
        SettingsButton.Visibility = !_nativeTrayExposed && systemButtons.Settings ? Visibility.Visible : Visibility.Collapsed;
        NetworkButton.Visibility = !_nativeTrayExposed && systemButtons.Network ? Visibility.Visible : Visibility.Collapsed;
        InputMethodButton.Visibility = !_nativeTrayExposed && systemButtons.InputMethod ? Visibility.Visible : Visibility.Collapsed;
        OnScreenKeyboardButton.Visibility = !_nativeTrayExposed && systemButtons.OnScreenKeyboard ? Visibility.Visible : Visibility.Collapsed;
        EmojiButton.Visibility = !_nativeTrayExposed && systemButtons.Emoji ? Visibility.Visible : Visibility.Collapsed;
        TrayButton.Visibility = !_nativeTrayExposed && systemButtons.Tray ? Visibility.Visible : Visibility.Collapsed;
        VolumeButton.Visibility = !_nativeTrayExposed && systemButtons.Volume ? Visibility.Visible : Visibility.Collapsed;
        MicrophoneButton.Visibility = !_nativeTrayExposed && systemButtons.Microphone && _hasMicrophoneDevice ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded && systemButtons.Microphone && !_nativeTrayExposed) _microphoneRefreshTimer.Start();
        else _microphoneRefreshTimer.Stop();
        WidgetsButton.Visibility = systemButtons.Widgets ? Visibility.Visible : Visibility.Collapsed;
        TaskViewButton.Visibility = systemButtons.TaskView ? Visibility.Visible : Visibility.Collapsed;
        ShowDesktopButton.Visibility = systemButtons.ShowDesktop ? Visibility.Visible : Visibility.Collapsed;
        ClockButton.Visibility = !_nativeTrayExposed && systemButtons.Clock ? Visibility.Visible : Visibility.Collapsed;
        CloseBarButton.Width = _nativeTrayExposed ? 32 : double.NaN;
        CloseBarButton.Height = _nativeTrayExposed ? 32 : double.NaN;
        CloseBarButton.Padding = _nativeTrayExposed ? new Thickness(0) : new Thickness(12, 7, 12, 7);
        CloseBarButton.Margin = _nativeTrayExposed ? new Thickness(0) : new Thickness(2, 0, 2, 0);
        RootBorder.Padding = _nativeTrayExposed && _edge is TaskbarEdge.Bottom or TaskbarEdge.Top
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
            StartControls.Orientation = Orientation.Vertical;
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
            StartControls.Orientation = Orientation.Horizontal;
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
            RootBorder.Background = TaskbarTheme.CreateBackground(_isDark, GetEffectiveTransparency());
            RootBorder.BorderThickness = new Thickness(1);
            ResetSegments();
        }
        else if (_preferences.TaskbarLayout is TaskbarStyle.Segmented or TaskbarStyle.DockLike)
        {
            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.Background = Brushes.Transparent;
            RootBorder.BorderBrush = Brushes.Transparent;
            RootBorder.BorderThickness = new Thickness(0);
            ResetSegments();
            StyleSegment(AppsSegment, vertical);
            if (_preferences.TaskbarLayout == TaskbarStyle.DockLike)
            {
                AppsSegment.HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
                AppsSegment.VerticalAlignment = vertical ? VerticalAlignment.Center : VerticalAlignment.Stretch;
            }
            else
            {
                StyleSegment(StartSegment, vertical);
                StyleSegment(SystemSegment, vertical);
            }
        }
        else
        {
            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.Background = TaskbarTheme.CreateBackground(_isDark, GetEffectiveTransparency());
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
            segment.HorizontalAlignment = HorizontalAlignment.Stretch;
            segment.VerticalAlignment = VerticalAlignment.Stretch;
            segment.Background = Brushes.Transparent;
            segment.BorderBrush = Brushes.Transparent;
            segment.BorderThickness = new Thickness(0);
            segment.CornerRadius = new CornerRadius(0);
            segment.Margin = new Thickness(0);
            segment.Padding = new Thickness(0);
        }
    }

    private void UpdateSystemBackdrop()
    {
        if (!_nativeReady) return;
        var shouldApplyBackdrop = _preferences.TaskbarLayout != TaskbarStyle.DockLike;
        if (_systemBackdropForCurrentStyle == shouldApplyBackdrop) return;

        var handle = new WindowInteropHelper(this).Handle;
        if (shouldApplyBackdrop) SystemBackdropService.TryApplyTransientBackdrop(handle);
        else SystemBackdropService.TryClearSystemBackdrop(handle);
        _systemBackdropForCurrentStyle = shouldApplyBackdrop;
    }

    private void StyleSegment(Border segment, bool vertical)
    {
        segment.Background = TaskbarTheme.CreateBackground(_isDark, GetEffectiveTransparency());
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
        UpdateBatteryStatus();
        UpdateMicrophoneStatus();
        _refreshTimer.Start();
        _batteryRefreshTimer.Start();
        if (_autoHide || _autoHideWhenMaximized) _autoHideTimer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.RemoveHook(WindowProc);
        _refreshTimer.Stop();
        _batteryRefreshTimer.Stop();
        _microphoneRefreshTimer.Stop();
        _autoHideTimer.Stop();
        _previewOpenTimer.Stop();
        _previewCloseTimer.Stop();
        _nativeAppBar.Dispose();
        _previewWindow?.Close();
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_nativeAppBar.IsRegistered && message == 0x0006)
            _nativeAppBar.NotifyActivated();
        else if (_nativeAppBar.IsRegistered && message == 0x0047)
            _nativeAppBar.NotifyWindowPositionChanged();

        if (message == NativeTaskbarAppBarService.CallbackMessage
            && unchecked((int)wParam.ToInt64()) == NativeTaskbarAppBarService.PositionChangedNotification
            && _nativeAppBar.IsRegistered)
        {
            ApplyLayout();
            handled = true;
            return 0;
        }
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
            UpdateClock();
        }));
    }

    private void RefreshWindows()
    {
        var allWindows = _windowOrder.Synchronize(_windows.Enumerate());
        var virtualDesktopWindows = TaskbarVirtualDesktopPolicy.Filter(allWindows, _preferences.TaskbarShowWindowsFromAllVirtualDesktops);
        var wasMaximizedWindowOnDisplay = _maximizedWindowOnDisplay;
        _maximizedWindowOnDisplay = TaskbarAutoHidePolicy.HasMaximizedWindowOnDisplay(virtualDesktopWindows, Display);
        var layoutChanged = _preferences.TaskbarDynamicTransparency && wasMaximizedWindowOnDisplay != _maximizedWindowOnDisplay;
        if (wasMaximizedWindowOnDisplay && !_maximizedWindowOnDisplay && !_autoHide && _collapsed)
        {
            _collapsed = false;
            layoutChanged = true;
        }
        if (layoutChanged) ApplyLayout();
        var windows = _preferences.TaskbarWindowDisplayMode == TaskbarWindowDisplayMode.AllTaskbars
            ? virtualDesktopWindows
            : TaskbarWindowDisplayPolicy.Filter(virtualDesktopWindows, Display, TaskbarDisplayService.Enumerate(), _preferences.TaskbarWindowDisplayMode);
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
        RightControls.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var reservedControlsLength = vertical ? RightControls.DesiredSize.Height : RightControls.DesiredSize.Width;
        var reservedLength = reservedControlsLength + 84 + (_preferences.PinnedApps?.Count ?? 0) * buttonSpan;
        return Math.Max(1, (int)Math.Floor((availableLength - reservedLength) / buttonSpan));
    }

    private int GetEffectiveTransparency() => TaskbarTransparencyPolicy.GetEffectiveTransparency(
        _preferences.TaskbarTransparency, _preferences.TaskbarDynamicTransparency, _maximizedWindowOnDisplay);

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = TaskbarClockPolicy.FormatTime(now, showSeconds: TaskbarClockPolicy.ShouldShowSeconds());
        DateText.Text = TaskbarClockPolicy.FormatDate(now);
    }

    private void UpdateBatteryStatus()
    {
        var wasVisible = BatteryButton.Visibility == Visibility.Visible;
        try
        {
            _batteryStatus = TaskbarBatteryService.TryRead();
        }
        catch (Exception ex)
        {
            _batteryStatus = null;
            Trace.TraceWarning($"Could not read battery status: {ex}");
        }

        BatteryButton.Visibility = _preferences.ReplaceNativeTaskbar && _preferences.TaskbarSystemButtons!.Battery && _batteryStatus is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_batteryStatus is not { } status)
        {
            if (IsLoaded && wasVisible != (BatteryButton.Visibility == Visibility.Visible)) RefreshWindows();
            return;
        }

        BatteryFill.Width = TaskbarBatteryService.GetFillWidth(status.Percent, 13);
        BatteryFill.Background = status.Percent is <= 15 ? Brushes.IndianRed : TaskbarTheme.GetBrush("TaskbarAccentBrush");
        BatteryPercentText.Text = status.Percent is { } percent ? $"{percent}%" : string.Empty;
        BatteryChargingGlyph.Visibility = status.IsCharging ? Visibility.Visible : Visibility.Collapsed;
        BatteryButton.ToolTip = TaskbarBatteryService.GetLabel(status);
        if (IsLoaded && wasVisible != (BatteryButton.Visibility == Visibility.Visible)) RefreshWindows();
    }

    private void BatterySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:powersleep") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            BatteryButton.ToolTip = $"Could not open Power settings: {ex.Message}";
            Trace.TraceWarning($"Could not open Windows Power settings: {ex}");
        }
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
        if (_preferences.TaskbarLayout == TaskbarStyle.DockLike)
        {
            AppsSegment.BorderBrush = canPin ? TaskbarTheme.GetBrush("TaskbarAccentFallbackBrush") : TaskbarTheme.GetBrush("TaskbarSegmentBorderBrush");
            AppsSegment.Background = canPin ? TaskbarTheme.GetBrush("TaskbarHoverBrush") : TaskbarTheme.CreateBackground(_isDark, GetEffectiveTransparency());
        }
        else
        {
            RootBorder.BorderBrush = canPin ? TaskbarTheme.GetBrush("TaskbarAccentFallbackBrush") : TaskbarTheme.GetBrush("TaskbarBorderBrush");
            RootBorder.Background = canPin ? TaskbarTheme.GetBrush("TaskbarHoverBrush") : TaskbarTheme.CreateBackground(_isDark, GetEffectiveTransparency());
        }
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
        if (_preferences.TaskbarLayout == TaskbarStyle.DockLike)
        {
            RootBorder.BorderBrush = Brushes.Transparent;
            RootBorder.Background = Brushes.Transparent;
            AppsSegment.BorderBrush = TaskbarTheme.GetBrush("TaskbarSegmentBorderBrush");
            AppsSegment.Background = TaskbarTheme.CreateBackground(_isDark, GetEffectiveTransparency());
        }
        else if (_preferences.TaskbarLayout == TaskbarStyle.Segmented)
        {
            RootBorder.BorderBrush = Brushes.Transparent;
            RootBorder.Background = Brushes.Transparent;
        }
        else
        {
            RootBorder.BorderBrush = TaskbarTheme.GetBrush("TaskbarBorderBrush");
            RootBorder.Background = TaskbarTheme.CreateBackground(_isDark, GetEffectiveTransparency());
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
        TaskbarAppButton_MouseLeave(sender, e);
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
        var preview = new TaskbarPreviewWindow(group.Windows, Display, _edge, target, MovePreviewWindow) { ShowActivated = activate };
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

    private void MovePreviewWindow(RunningWindow moving, RunningWindow target)
    {
        var windows = _windows.Enumerate();
        if (!_windowOrder.MoveWindow(windows, moving, target)) return;
        var orderedWindows = _windowOrder.Synchronize(_windows.Enumerate());
        RefreshWindows();
        _previewWindow?.ApplyWindowOrder(orderedWindows);
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

    private void WindowButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle
            || sender is not Button { Tag: TaskbarWindowGroup group }
            || TaskbarWindowGrouping.SelectCloseTarget(group) is not { } target) return;
        RunningWindowService.Close(target);
        e.Handled = true;
    }

    private void TaskbarAppButton_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not TaskbarButtonViewModel { DynamicAura: true, AuraBrush: RadialGradientBrush brush } ||
            button.ActualWidth <= 0 || button.ActualHeight <= 0) return;
        var point = e.GetPosition(button);
        var center = new Point(Math.Clamp(point.X / button.ActualWidth, 0, 1), Math.Clamp(point.Y / button.ActualHeight, 0, 1));
        brush.Center = center;
        brush.GradientOrigin = center;
    }

    private void TaskbarAppButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button { DataContext: TaskbarButtonViewModel { AuraBrush: RadialGradientBrush brush } })
        {
            brush.Center = new Point(0.5, 0.5);
            brush.GradientOrigin = new Point(0.5, 0.5);
        }
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

    private void OpenPinnedLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: PinnedTaskbarApp { CanOpenLocation: true } app }) return;
        try
        {
            System.Diagnostics.Process.Start(TaskbarPinCatalog.BuildLocationLaunchInfo(app));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not show the location for {app.Name}.\n\n{ex.Message}", "Could not open file location", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RunPinnedAsAdministrator_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: PinnedTaskbarApp { CanRunElevated: true } app }) return;
        try
        {
            System.Diagnostics.Process.Start(TaskbarPinCatalog.BuildElevatedLaunchInfo(app));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not start {app.Name} with administrator privileges.\n\n{ex.Message}", "Could not start elevated app", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RunWindowAsAdministrator_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        var window = item.Tag switch
        {
            RunningWindow runningWindow => runningWindow,
            TaskbarWindowGroup group => group.Windows.FirstOrDefault(),
            _ => null
        };
        if (window is null) return;

        var app = new AppEntry(window.ApplicationName, window.ExecutablePath);
        if (!TaskbarPinCatalog.CanRunAsAdministrator(app.Name, app.ShortcutPath)) return;
        try
        {
            AppCatalogService.LaunchAsAdministrator(app);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not start {app.Name} with administrator privileges.\n\n{ex.Message}", "Could not start elevated app", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PinnedFolderMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: PinnedTaskbarApp { IsDirectory: true } app } menu) return;
        PopulatePinnedFolderMenu(menu, app.ExecutablePath, depth: 0);
    }

    private void PinnedFolderSubmenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path } menu || !int.TryParse(menu.Uid, out var depth)) return;
        PopulatePinnedFolderMenu(menu, path, depth);
    }

    private void PopulatePinnedFolderMenu(MenuItem menu, string path, int depth)
    {
        menu.Items.Clear();
        if (!Directory.Exists(path))
        {
            menu.Items.Add(new MenuItem { Header = "Folder is unavailable", IsEnabled = false });
            return;
        }

        var openFolderItem = new MenuItem { Header = "Open in File Explorer", Tag = path };
        openFolderItem.Click += OpenFolderMenuEntry_Click;
        menu.Items.Add(openFolderItem);
        menu.Items.Add(new Separator());
        try
        {
            var entries = TaskbarFolderMenuCatalog.ReadChildren(path);
            if (entries.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No visible items", IsEnabled = false });
            foreach (var entry in entries)
            {
                var item = new MenuItem { Header = entry.Name, Tag = entry.FullPath };
                if (entry.IsDirectory && !entry.IsReparsePoint && depth < 2)
                {
                    item.Uid = (depth + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    item.SubmenuOpened += PinnedFolderSubmenu_SubmenuOpened;
                    item.Items.Add(new MenuItem { Header = "Loading...", IsEnabled = false });
                }
                else
                {
                    item.Click += OpenFolderMenuEntry_Click;
                }
                menu.Items.Add(item);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not read taskbar folder menu '{path}': {ex.Message}");
            menu.Items.Add(new MenuItem { Header = "Could not read this folder", IsEnabled = false });
        }
    }

    private void OpenFolderMenuEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path }) return;
        try
        {
            var startInfo = Directory.Exists(path)
                ? new ProcessStartInfo("explorer.exe") { UseShellExecute = true }
                : new ProcessStartInfo(path) { UseShellExecute = true };
            if (Directory.Exists(path)) startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Windows could not open {path}.\n\n{ex.Message}", "Could not open folder item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PinnedButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PinnedTaskbarApp app }) return;
        ActivatePinnedApp(app, sender as Button);
    }

    public bool TryActivatePinnedApp(int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > (_preferences.PinnedApps?.Count ?? 0)) return false;
        ActivatePinnedApp(_preferences.PinnedApps![oneBasedIndex - 1], showPreview: false, toggleMinimizeOnActive: false);
        return true;
    }

    public void FocusTaskbar()
    {
        if (!_nativeReady) return;
        _autoHideTimer.Stop();
        if (!_keyboardFocusActive) _previousForegroundWindow = GetForegroundWindow();
        if (_collapsed)
        {
            _collapsed = false;
            ApplyLayout();
        }
        Activate();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var buttons = FindVisualChildren<Button>(LayoutGrid).Where(button => button.IsVisible && button.IsEnabled && button.Focusable).ToList();
            var currentIndex = buttons.FindIndex(button => button.IsKeyboardFocused);
            var target = _keyboardFocusActive && currentIndex >= 0
                ? buttons[TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(currentIndex, buttons.Count, forward: true)!.Value]
                : FindVisualChildren<Button>(PinnedItems).FirstOrDefault(button => button.IsVisible && button.IsEnabled)
                    ?? FindVisualChildren<Button>(WindowItems).FirstOrDefault(button => button.IsVisible && button.IsEnabled)
                    ?? StartButton;
            _keyboardFocusActive = true;
            target.Focus();
            Keyboard.Focus(target);
        }), DispatcherPriority.Input);
    }

    private void Taskbar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_keyboardFocusActive) return;
        if (e.Key == Key.Escape)
        {
            _keyboardFocusActive = false;
            if (_previousForegroundWindow != 0) SetForegroundWindow(_previousForegroundWindow);
            _previousForegroundWindow = 0;
            e.Handled = true;
            return;
        }

        var isVertical = _edge is TaskbarEdge.Left or TaskbarEdge.Right;
        var forwardKey = isVertical ? Key.Down : Key.Right;
        var backwardKey = isVertical ? Key.Up : Key.Left;
        var buttons = FindVisualChildren<Button>(LayoutGrid).Where(button => button.IsVisible && button.IsEnabled && button.Focusable).ToList();
        if (buttons.Count == 0) return;
        var currentIndex = buttons.FindIndex(button => button.IsKeyboardFocused);
        int targetIndex;
        if (e.Key == forwardKey) targetIndex = TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(currentIndex, buttons.Count, forward: true)!.Value;
        else if (e.Key == backwardKey) targetIndex = TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(currentIndex, buttons.Count, forward: false)!.Value;
        else if (e.Key == Key.Home) targetIndex = 0;
        else if (e.Key == Key.End) targetIndex = buttons.Count - 1;
        else return;
        buttons[targetIndex].Focus();
        e.Handled = true;
    }

    private void Taskbar_Deactivated(object? sender, EventArgs e)
    {
        _keyboardFocusActive = false;
        _previousForegroundWindow = 0;
        if (_autoHide || _autoHideWhenMaximized) _autoHideTimer.Start();
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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    private void ActivatePinnedApp(PinnedTaskbarApp app, Button? sourceButton = null, bool showPreview = true, bool toggleMinimizeOnActive = true)
    {
        var openWindows = _windows.Enumerate()
            .Where(window => _preferences.TaskbarShowWindowsFromAllVirtualDesktops || window.IsOnCurrentVirtualDesktop is not false)
            .Where(window => TaskbarWindowGrouping.MatchesPinnedApp(app, window))
            .ToList();
        if (_preferences.TaskbarGrouping == TaskbarGroupingMode.Never && openWindows.Count > 1 &&
            TaskbarWindowGrouping.SelectPinnedRepresentative(app, openWindows) is { } representative)
        {
            openWindows = [representative];
        }
        if (openWindows.Count > 1 && showPreview && sourceButton is not null)
        {
            ShowWindowPreview(new TaskbarWindowGroup(app.Name, app.Name, openWindows), sourceButton, activate: true);
            return;
        }
        if (openWindows.Count == 1)
        {
            if (toggleMinimizeOnActive) RunningWindowService.ActivateOrMinimize(openWindows[0]);
            else RunningWindowService.Activate(openWindows[0]);
            return;
        }
        if (openWindows.Count > 1)
        {
            if (toggleMinimizeOnActive) RunningWindowService.ActivateOrMinimize(openWindows[0]);
            else RunningWindowService.Activate(openWindows[0]);
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

    private void NetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(SystemFlyoutService.NetworkSettingsUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open Network settings", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void InputMethod_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenInputMethodSwitcher();

    private void OnScreenKeyboard_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenOnScreenKeyboard();

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

    private void AudioInputContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Items.Clear();
        try
        {
            var inputs = AudioEndpointVolumeService.EnumerateInputs();
            if (inputs.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No active input devices", IsEnabled = false });
            else
            {
                foreach (var input in inputs)
                {
                    var item = new MenuItem
                    {
                        Header = input.Name,
                        Tag = input,
                        IsCheckable = true,
                        IsChecked = input.IsDefault,
                        IsEnabled = !input.IsDefault,
                        ToolTip = input.IsDefault ? "Current default input" : null
                    };
                    item.Click += AudioInputDevice_Click;
                    menu.Items.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            menu.Items.Add(new MenuItem { Header = "Could not list input devices", IsEnabled = false, ToolTip = ex.Message });
            Trace.TraceWarning($"Could not enumerate audio input devices: {ex}");
        }

        menu.Items.Add(new Separator());
        var settingsItem = new MenuItem { Header = "Sound settings…" };
        settingsItem.Click += SoundSettings_Click;
        menu.Items.Add(settingsItem);
    }

    private void AudioInputDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AudioInputDevice input }) return;
        try
        {
            AudioEndpointVolumeService.SetDefaultInput(input.Id);
            UpdateMicrophoneStatus();
        }
        catch (Exception ex)
        {
            MicrophoneButton.ToolTip = $"Could not select input device: {ex.Message} · Open Sound settings";
            Trace.TraceWarning($"Could not set the default audio input to '{input.Name}': {ex}");
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

    private void MicrophoneButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        try
        {
            AudioEndpointVolumeService.ToggleDefaultInputMute();
            UpdateMicrophoneStatus();
        }
        catch (Exception ex)
        {
            MicrophoneButton.ToolTip = $"Could not change microphone mute: {ex.Message}";
            Trace.TraceWarning($"Could not toggle default audio input mute: {ex}");
        }
        e.Handled = true;
    }

    private void MicrophoneButton_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            var state = AudioEndpointVolumeService.ReadDefaultInput();
            var level = AudioVolumePolicy.Adjust(state.Volume, e.Delta);
            AudioEndpointVolumeService.SetDefaultInputVolume(level);
            UpdateMicrophoneStatus();
        }
        catch (Exception ex)
        {
            MicrophoneButton.ToolTip = $"Could not adjust microphone level: {ex.Message}";
            Trace.TraceWarning($"Could not adjust default audio input level: {ex}");
        }
        e.Handled = true;
    }

    private void UpdateMicrophoneStatus()
    {
        var wasVisible = MicrophoneButton.Visibility == Visibility.Visible;
        var previouslyAvailable = _hasMicrophoneDevice;
        try
        {
            _microphoneStatus = AudioEndpointVolumeService.ReadDefaultInput();
            _hasMicrophoneDevice = true;
            var status = _microphoneStatus.Value;
            MicrophoneButton.Content = status.Muted ? "🔇" : "🎤";
            MicrophoneButton.ToolTip = AudioVolumePolicy.GetMicrophoneLabel(status.Volume, status.Muted) + " · Scroll to adjust · Middle-click to mute · Right-click to choose input · Click for Sound settings";
        }
        catch (Exception ex)
        {
            _microphoneStatus = null;
            try
            {
                _hasMicrophoneDevice = AudioEndpointVolumeService.EnumerateInputs().Count > 0;
                _reportedMicrophoneEnumerationFailure = false;
            }
            catch (Exception enumerationException)
            {
                _hasMicrophoneDevice = false;
                if (!_reportedMicrophoneEnumerationFailure)
                    Trace.TraceWarning($"Could not enumerate audio input devices: {enumerationException}");
                _reportedMicrophoneEnumerationFailure = true;
            }

            MicrophoneButton.Content = "🎤";
            MicrophoneButton.ToolTip = _hasMicrophoneDevice
                ? "Microphone level control unavailable · Right-click to choose input · Click for Sound settings"
                : "No active microphone endpoint · Click for Sound settings";
            if (previouslyAvailable)
                Trace.TraceWarning($"Could not read the default microphone endpoint: {ex.Message}");
        }

        MicrophoneButton.Visibility = !_nativeTrayExposed && _preferences.TaskbarSystemButtons!.Microphone && _hasMicrophoneDevice
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (IsLoaded && wasVisible != (MicrophoneButton.Visibility == Visibility.Visible)) RefreshWindows();
    }

    private void Clock_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenNotificationCenter();

    private void Search_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenWindowsSearch();

    private void TaskView_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenTaskView();

    private void ShowDesktop_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.ShowDesktop();

    private void Tray_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.FocusNotificationArea();

    private void QuickSettings_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenQuickSettings();

    private void Emoji_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenEmojiPanel();

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
