using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopTuner;

public partial class StartMenuWindow : Window
{
    private readonly AppCatalogService _catalog = new();
    private IReadOnlyList<AppEntry> _apps = [];

    public StartMenuWindow()
    {
        InitializeComponent();
        _apps = _catalog.FindStartMenuApps();
        RefreshApps();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshApps();

    private void RefreshApps()
    {
        var query = SearchBox?.Text.Trim() ?? string.Empty;
        var results = _apps.AsEnumerable();
        if (query.Length > 0)
        {
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            results = results.Where(entry => terms.All(term => entry.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(entry => entry.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        var shown = results.Take(40).ToList();
        AppList.ItemsSource = shown;
        ResultsHeading.Text = query.Length == 0 ? "All apps" : "Search results";
        ResultCount.Text = shown.Count == 40 ? "40+" : shown.Count.ToString();
        EmptyMessage.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (shown.Count > 0 && AppList.SelectedIndex < 0) AppList.SelectedIndex = 0;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            LaunchSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin && AppList.Items.Count > 0)
        {
            AppList.Focus();
            AppList.SelectedIndex = Math.Max(AppList.SelectedIndex, 0);
            e.Handled = true;
        }
    }

    private void AppList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelected();

    private void LaunchSelected()
    {
        if (AppList.SelectedItem is not AppEntry entry) return;
        try
        {
            AppCatalogService.Launch(entry);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not open {entry.Name}.\n\n{ex.Message}", "Could not launch app", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void QuickLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        try
        {
            var target = action switch
            {
                "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "settings" => "ms-settings:",
                "power" => "ms-settings:powersleep",
                _ => throw new InvalidOperationException("Unknown shortcut.")
            };
            AppCatalogService.OpenLocation(target);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open location", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
