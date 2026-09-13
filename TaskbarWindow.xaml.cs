using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DesktopTuner;

public partial class TaskbarWindow : Window
{
    private readonly RunningWindowService _windows = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly Action _showStartMenu;

    public TaskbarWindow(Action showStartMenu, TaskbarEdge edge)
    {
        InitializeComponent();
        _showStartMenu = showStartMenu;
        SetEdge(edge);
        _refreshTimer.Tick += (_, _) => RefreshWindows();
    }

    public void SetEdge(TaskbarEdge edge)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var vertical = edge is TaskbarEdge.Left or TaskbarEdge.Right;
        LayoutGrid.ColumnDefinitions.Clear();
        LayoutGrid.RowDefinitions.Clear();
        if (vertical)
        {
            Width = 176;
            Height = screenHeight;
            Left = edge == TaskbarEdge.Left ? 0 : screenWidth - Width;
            Top = 0;
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
            WindowItems.ItemsPanel = (ItemsPanelTemplate)FindResource("VerticalWindowPanel");
            WindowScroller.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
            WindowScroller.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            WindowScroller.Margin = new Thickness(0, 10, 0, 10);
            RootBorder.Padding = new Thickness(5, 10, 5, 10);
            RootBorder.BorderThickness = edge == TaskbarEdge.Left ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
        }
        else
        {
            Width = screenWidth;
            Height = 54;
            Left = 0;
            Top = edge == TaskbarEdge.Top ? 0 : screenHeight - Height;
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
            WindowItems.ItemsPanel = (ItemsPanelTemplate)FindResource("HorizontalWindowPanel");
            WindowScroller.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            WindowScroller.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
            WindowScroller.Margin = new Thickness(10, 0, 10, 0);
            RootBorder.Padding = new Thickness(10, 4, 10, 4);
            RootBorder.BorderThickness = edge == TaskbarEdge.Top ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshWindows();
        UpdateClock();
        _refreshTimer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e) => _refreshTimer.Stop();

    private void RefreshWindows()
    {
        var windows = _windows.Enumerate();
        WindowItems.ItemsSource = windows;
        EmptyText.Visibility = windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateClock();
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("h:mm tt");

    private void Start_Click(object sender, RoutedEventArgs e) => _showStartMenu();

    private void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RunningWindow window }) RunningWindowService.Activate(window);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: RunningWindow window }) RunningWindowService.Minimize(window);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open Settings", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CloseBar_Click(object sender, RoutedEventArgs e) => Close();
}
