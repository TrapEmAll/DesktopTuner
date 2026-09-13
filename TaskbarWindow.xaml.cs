using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTuner;

public partial class TaskbarWindow : Window
{
    private readonly RunningWindowService _windows = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly Action _showStartMenu;
    private readonly Func<bool> _isStartMenuVisible;
    private readonly Action<DesktopPreferences> _persistPreferences;
    private DesktopPreferences _preferences = new(TaskbarEdge.Bottom);
    private TaskbarEdge _edge;
    private TaskbarSize _size;
    private bool _autoHide;
    private bool _collapsed;

    public TaskbarWindow(Action showStartMenu, Func<bool> isStartMenuVisible, DesktopPreferences preferences, Action<DesktopPreferences> persistPreferences)
    {
        InitializeComponent();
        _showStartMenu = showStartMenu;
        _isStartMenuVisible = isStartMenuVisible;
        _persistPreferences = persistPreferences;
        _refreshTimer.Tick += (_, _) => RefreshWindows();
        _autoHideTimer.Tick += (_, _) => AutoHideTimer_Tick();
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
        if (!_autoHide) _autoHideTimer.Stop();
        else if (IsLoaded) _autoHideTimer.Start();
    }

    private void ApplyLayout()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var bounds = TaskbarLayoutCalculator.Calculate(screenWidth, screenHeight, new DesktopPreferences(_edge, _size, _autoHide), _collapsed);
        var vertical = _edge is TaskbarEdge.Left or TaskbarEdge.Right;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        LayoutGrid.ColumnDefinitions.Clear();
        LayoutGrid.RowDefinitions.Clear();
        if (vertical)
        {
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(StartButton, 0);
            Grid.SetColumn(StartButton, 0);
            Grid.SetRow(WindowScroller, 1);
            Grid.SetColumn(WindowScroller, 0);
            Grid.SetRow(EmptyText, 1);
            Grid.SetColumn(EmptyText, 0);
            Grid.SetRow(RightControls, 2);
            Grid.SetColumn(RightControls, 0);
            RightControls.Orientation = Orientation.Vertical;
            TaskButtonsStack.Orientation = Orientation.Vertical;
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
            Grid.SetRow(StartButton, 0);
            Grid.SetColumn(StartButton, 0);
            Grid.SetRow(WindowScroller, 0);
            Grid.SetColumn(WindowScroller, 1);
            Grid.SetRow(EmptyText, 0);
            Grid.SetColumn(EmptyText, 1);
            Grid.SetRow(RightControls, 0);
            Grid.SetColumn(RightControls, 2);
            RightControls.Orientation = Orientation.Horizontal;
            TaskButtonsStack.Orientation = Orientation.Horizontal;
            PinnedItems.ItemsPanel = (ItemsPanelTemplate)FindResource("HorizontalWindowPanel");
            WindowItems.ItemsPanel = (ItemsPanelTemplate)FindResource("HorizontalWindowPanel");
            WindowScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            WindowScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            WindowScroller.Margin = new Thickness(10, 0, 10, 0);
            RootBorder.Padding = new Thickness(10, 4, 10, 4);
            RootBorder.BorderThickness = _edge == TaskbarEdge.Top ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
            PinDivider.Width = 1;
            PinDivider.Height = 24;
            PinDivider.Margin = new Thickness(5, 0, 5, 0);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshWindows();
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
        PinnedItems.ItemsSource = _preferences.PinnedApps;
        WindowItems.ItemsSource = windows;
        EmptyText.Visibility = windows.Count == 0 && _preferences.PinnedApps!.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateClock();
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("h:mm tt");

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
        var canPin = GetDroppableExecutables(e.Data).Any();
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
        var pins = TaskbarPinCatalog.AddDroppedFiles(currentPins, GetDroppableExecutables(e.Data));
        if (pins.Count == currentPins.Count) return;

        _preferences = _preferences with { PinnedApps = pins };
        _persistPreferences(_preferences);
        RefreshWindows();
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private IEnumerable<string> GetDroppableExecutables(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] paths)
            return [];

        return paths.Where(File.Exists)
            .Where(path => TaskbarPinCatalog.AddDroppedFiles(_preferences.PinnedApps ?? [], [path]).Count > (_preferences.PinnedApps?.Count ?? 0));
    }

    private void ResetDropHighlight()
    {
        RootBorder.BorderBrush = Brush("#405064");
        RootBorder.Background = Brush("#F2171D2A");
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void AutoHideTimer_Tick()
    {
        if (!TaskbarAutoHidePolicy.ShouldCollapse(_autoHide, IsMouseOver, _isStartMenuVisible())) return;
        _collapsed = true;
        ApplyLayout();
        _autoHideTimer.Stop();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => _showStartMenu();

    private void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RunningWindow window }) RunningWindowService.Activate(window);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: RunningWindow window }) RunningWindowService.Minimize(window);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var currentPins = _preferences.PinnedApps ?? [];
        if (sender is not MenuItem { Tag: RunningWindow window }) return;
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
        if (!File.Exists(app.ExecutablePath))
        {
            MessageBox.Show(this, $"The pinned app could not be found:\n{app.ExecutablePath}", "Pinned app unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(app.ExecutablePath) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not launch pinned app", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open Settings", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CloseBar_Click(object sender, RoutedEventArgs e) => Close();
}
