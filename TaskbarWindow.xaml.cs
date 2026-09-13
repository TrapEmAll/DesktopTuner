using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTuner;

public partial class TaskbarWindow : Window
{
    private const string PinnedAppDragFormat = "DesktopTuner.PinnedTaskbarApp";
    private readonly RunningWindowService _windows = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
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
    private bool _collapsed;
    private bool _nativeReady;
    private bool _nativeTrayExposed;
    private PinnedTaskbarApp? _pinDragCandidate;
    private Point _pinDragStart;

    public TaskbarDisplay Display { get; private set; }

    public TaskbarWindow(TaskbarDisplay display, Action<TaskbarDisplay> showStartMenu, Func<bool> isStartMenuVisible, DesktopPreferences preferences, Action<DesktopPreferences> persistPreferences, Action closeAllTaskbars, Action showSettings, Action quitApplication)
    {
        InitializeComponent();
        Display = display;
        _showStartMenu = showStartMenu;
        _isStartMenuVisible = isStartMenuVisible;
        _persistPreferences = persistPreferences;
        _closeAllTaskbars = closeAllTaskbars;
        _showSettings = showSettings;
        _quitApplication = quitApplication;
        _refreshTimer.Tick += (_, _) => RefreshWindows();
        _autoHideTimer.Tick += (_, _) => AutoHideTimer_Tick();
        SourceInitialized += (_, _) =>
        {
            _nativeReady = true;
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
        _collapsed = _autoHide && !_isStartMenuVisible() && !IsMouseOver;
        ApplyLayout();
        if (IsLoaded) RefreshWindows();
        if (IsLoaded) Dispatcher.BeginInvoke(new Action(UpdateButtonCentering));
        if (!_autoHide) _autoHideTimer.Stop();
        else if (IsLoaded) _autoHideTimer.Start();
    }

    private void ApplyLayout()
    {
        var layoutPreferences = _preferences with { TaskbarEdge = _edge, TaskbarSize = _size, AutoHide = _autoHide };
        var bounds = TaskbarLayoutCalculator.Calculate(Display, layoutPreferences, _collapsed);
        var trayBounds = NativeTaskbarTrayService.FindTrayBounds(Display);
        var integratedBounds = TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(Display, layoutPreferences, trayBounds, _collapsed);
        _nativeTrayExposed = integratedBounds is not null;
        if (integratedBounds is { } trayIntegratedBounds) bounds = trayIntegratedBounds;
        SettingsButton.Visibility = _nativeTrayExposed ? Visibility.Collapsed : Visibility.Visible;
        TrayButton.Visibility = _nativeTrayExposed ? Visibility.Collapsed : Visibility.Visible;
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
            RootBorder.Background = Brush("#E6171D2A");
            RootBorder.BorderBrush = Brush("#66708D");
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
            RootBorder.Background = Brush("#F2171D2A");
            RootBorder.BorderBrush = Brush("#405064");
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

    private static void StyleSegment(Border segment, bool vertical)
    {
        segment.Background = Brush("#E6171D2A");
        segment.BorderBrush = Brush("#46516A");
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
        if (_autoHide) _autoHideTimer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _autoHideTimer.Stop();
    }

    private void RefreshWindows()
    {
        var windows = _windows.Enumerate();
        var vertical = _edge is TaskbarEdge.Left or TaskbarEdge.Right;
        PinnedItems.ItemsSource = _preferences.PinnedApps!.Select(app => TaskbarButtonViewModel.FromPin(app, _preferences, vertical)).ToList();
        WindowItems.ItemsSource = TaskbarWindowGrouping.Create(windows, _preferences.TaskbarGrouping, GetWindowButtonCapacity())
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
        var reservedLength = vertical ? 170 + (_preferences.PinnedApps?.Count ?? 0) * buttonSpan : 250 + (_preferences.PinnedApps?.Count ?? 0) * buttonSpan;
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
        if (_autoHide) _autoHideTimer.Start();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var canPin = GetDroppableItems(e.Data).Any();
        e.Effects = canPin ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        RootBorder.BorderBrush = canPin ? Brush("#8D86FF") : Brush("#405064");
        RootBorder.Background = canPin ? Brush("#302C49") : Brush("#F2171D2A");
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
        RootBorder.BorderBrush = Brush("#405064");
        RootBorder.Background = Brush("#F2171D2A");
    }

    private void PinnedButton_DragOver(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: PinnedTaskbarApp targetApp } button &&
            e.Data.GetDataPresent(PinnedAppDragFormat) && e.Data.GetData(PinnedAppDragFormat) is string sourcePath)
        {
            var canReorder = !string.Equals(sourcePath, targetApp.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            button.Background = canReorder ? Brush("#494F70") : Brushes.Transparent;
            e.Effects = canReorder ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (sender is Button { Tag: PinnedTaskbarApp app } && !app.IsDirectory &&
            File.Exists(app.ExecutablePath) && GetDroppedDocuments(e.Data).Any())
        {
            ((Button)sender).Background = Brush("#494F70");
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

    private static IEnumerable<string> GetDroppedDocuments(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] paths) return [];
        return paths.Where(File.Exists).Where(path => !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void AutoHideTimer_Tick()
    {
        if (!TaskbarAutoHidePolicy.ShouldCollapse(_autoHide, IsMouseOver, _isStartMenuVisible())) return;
        _collapsed = true;
        ApplyLayout();
        _autoHideTimer.Stop();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => _showStartMenu(Display);

    private void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TaskbarWindowGroup group }) return;
        if (group.Windows.Count == 1)
        {
            RunningWindowService.Activate(group.Windows[0]);
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = _edge switch
            {
                TaskbarEdge.Top => PlacementMode.Bottom,
                TaskbarEdge.Left => PlacementMode.Right,
                TaskbarEdge.Right => PlacementMode.Left,
                _ => PlacementMode.Top
            }
        };
        foreach (var window in group.Windows)
        {
            var item = new MenuItem { Header = window.Title, Tag = window, ToolTip = window.ExecutablePath };
            item.Click += GroupWindow_Click;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private static void GroupWindow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: RunningWindow window }) RunningWindowService.Activate(window);
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
        var openWindow = _windows.Enumerate().FirstOrDefault(window => string.Equals(window.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (openWindow is not null)
        {
            RunningWindowService.Activate(openWindow);
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

    private void Clock_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenNotificationCenter();

    private void Tray_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.FocusNotificationArea();

    private void Widgets_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenWidgets();

    private void TaskbarContextMenu_Opened(object sender, RoutedEventArgs e) => AutoHideMenuItem.IsChecked = _autoHide;

    private void ShowSettings_Click(object sender, RoutedEventArgs e) => _showSettings();

    private void Quit_Click(object sender, RoutedEventArgs e) => _quitApplication();

    private void AutoHideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var preferences = _preferences with { AutoHide = AutoHideMenuItem.IsChecked };
        _persistPreferences(preferences);
    }

    private void CloseBar_Click(object sender, RoutedEventArgs e) => _closeAllTaskbars();
}
