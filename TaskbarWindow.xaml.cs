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

    public TaskbarWindow(Action showStartMenu)
    {
        InitializeComponent();
        _showStartMenu = showStartMenu;
        Width = SystemParameters.PrimaryScreenWidth;
        Left = 0;
        Top = SystemParameters.PrimaryScreenHeight - Height;
        _refreshTimer.Tick += (_, _) => RefreshWindows();
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
