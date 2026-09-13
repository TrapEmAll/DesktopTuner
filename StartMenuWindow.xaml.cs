using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopTuner;

public partial class StartMenuWindow : Window
{
    private readonly AppCatalogService _catalog = new();
    private IReadOnlyList<AppEntry> _apps = [];
    private StartMenuStyle _style = StartMenuStyle.Modern;

    public StartMenuWindow(StartMenuStyle style)
    {
        InitializeComponent();
        _apps = _catalog.FindStartMenuApps();
        SetStyle(style);
    }

    public void SetStyle(StartMenuStyle style)
    {
        _style = style;
        MenuLayout.RowDefinitions.Clear();
        MenuLayout.ColumnDefinitions.Clear();
        var classic = style == StartMenuStyle.Classic;
        MenuLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (classic) MenuLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });

        if (classic)
        {
            MenuLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            MenuLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            MenuLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Width = 650;
            Height = 590;
            OuterBorder.CornerRadius = new CornerRadius(8);
            OuterBorder.BorderBrush = Brush("#30394D");
            OuterBorder.Background = Brush("#F0F2F7");
            HeaderTitle.Text = "Start";
            HeaderSubtitle.Text = "Search and launch apps";
            HeaderSubtitle.Visibility = Visibility.Visible;
            HeaderPanel.Margin = new Thickness(2, 0, 12, 12);
            SearchBox.Height = 42;
            AppPanel.Margin = new Thickness(0, 10, 12, 0);
            ResultsHeading.Text = "All programs";
            Grid.SetRow(HeaderPanel, 0);
            Grid.SetColumn(HeaderPanel, 0);
            Grid.SetRow(SearchBox, 1);
            Grid.SetColumn(SearchBox, 0);
            Grid.SetRow(AppPanel, 2);
            Grid.SetColumn(AppPanel, 0);
            Grid.SetRow(QuickLinksBorder, 0);
            Grid.SetColumn(QuickLinksBorder, 1);
            Grid.SetRowSpan(QuickLinksBorder, 3);
            QuickLinksBorder.Background = Brush("#252C3D");
            QuickLinksBorder.BorderBrush = Brush("#252C3D");
            QuickLinksBorder.BorderThickness = new Thickness(0);
            QuickLinksBorder.Padding = new Thickness(14, 14, 10, 14);
            QuickLinksStack.Orientation = Orientation.Vertical;
            QuickLinksTitle.Visibility = Visibility.Visible;
            SetQuickLinkAppearance(classic: true);
        }
        else
        {
            MenuLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            MenuLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            MenuLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            MenuLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(HeaderPanel, 0);
            Grid.SetColumn(HeaderPanel, 0);
            Grid.SetRow(SearchBox, 1);
            Grid.SetColumn(SearchBox, 0);
            Grid.SetRow(AppPanel, 2);
            Grid.SetColumn(AppPanel, 0);
            Grid.SetRow(QuickLinksBorder, 3);
            Grid.SetColumn(QuickLinksBorder, 0);
            Grid.SetRowSpan(QuickLinksBorder, 1);
            QuickLinksBorder.Background = Brushes.Transparent;
            QuickLinksBorder.BorderBrush = Brush("#E5E8EF");
            QuickLinksBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            QuickLinksBorder.Padding = new Thickness(0, style == StartMenuStyle.Compact ? 7 : 12, 0, 0);
            QuickLinksStack.Orientation = Orientation.Horizontal;
            QuickLinksTitle.Visibility = Visibility.Collapsed;
            SetQuickLinkAppearance(classic: false);

            if (style == StartMenuStyle.Compact)
            {
                Width = 370;
                Height = 500;
                OuterBorder.CornerRadius = new CornerRadius(14);
                OuterBorder.BorderBrush = Brush("#DDE2EB");
                OuterBorder.Background = Brush("#F9FAFD");
                HeaderTitle.Text = "Quick launch";
                HeaderSubtitle.Visibility = Visibility.Collapsed;
                HeaderPanel.Margin = new Thickness(0, 0, 0, 10);
                SearchBox.Height = 40;
                SearchBox.FontSize = 13;
                AppPanel.Margin = new Thickness(0, 8, 0, 4);
                ResultsHeading.Text = "Apps";
                foreach (var button in QuickLinksStack.Children.OfType<Button>())
                {
                    button.Padding = new Thickness(5, 5, 5, 5);
                    button.FontSize = 10;
                    button.Margin = new Thickness(0, 0, 4, 0);
                }
            }
            else
            {
                Width = 470;
                Height = 650;
                OuterBorder.CornerRadius = new CornerRadius(18);
                OuterBorder.BorderBrush = Brush("#DDE2EB");
                OuterBorder.Background = Brush("#F9FAFD");
                HeaderTitle.Text = "Good to see you";
                HeaderSubtitle.Text = "Search apps or open a favorite place";
                HeaderSubtitle.Visibility = Visibility.Visible;
                HeaderPanel.Margin = new Thickness(2, 0, 0, 18);
                SearchBox.Height = 46;
                SearchBox.FontSize = 14;
                AppPanel.Margin = new Thickness(0, 16, 0, 10);
                ResultsHeading.Text = "All apps";
                foreach (var button in QuickLinksStack.Children.OfType<Button>())
                {
                    button.Padding = button.Tag as string == "power" ? new Thickness(11, 7, 11, 7) : new Thickness(9, 7, 9, 7);
                    button.FontSize = 12;
                    button.Margin = button.Tag as string == "power" ? new Thickness(7, 0, 0, 0) : new Thickness(0, 0, 7, 0);
                }
            }
        }

        if (style != StartMenuStyle.Compact) SearchBox.FontSize = 14;
        RefreshApps();
    }

    private void SetQuickLinkAppearance(bool classic)
    {
        foreach (var button in QuickLinksStack.Children.OfType<Button>())
        {
            if (classic)
            {
                button.Background = Brush("#30394D");
                button.Foreground = Brushes.White;
                button.BorderBrush = Brush("#46516A");
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Padding = new Thickness(9, 9, 6, 9);
                button.FontSize = 11;
                button.Margin = new Thickness(0, 3, 0, 3);
            }
            else if (button.Tag as string == "power")
            {
                button.Background = Brush("#ECEBFA");
                button.Foreground = Brush("#5148C7");
                button.BorderBrush = Brush("#E1DFFF");
            }
            else
            {
                button.Background = Brushes.White;
                button.Foreground = Brush("#172033");
                button.BorderBrush = Brush("#E1E5EC");
                button.HorizontalContentAlignment = HorizontalAlignment.Center;
            }
        }
    }

    private static System.Windows.Media.SolidColorBrush Brush(string color) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

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
        var results = AppCatalogService.Search(_apps, query);
        var showFolders = _style == StartMenuStyle.Classic && query.Length == 0;
        AppTree.ItemsSource = showFolders ? AppCatalogService.BuildTree(_apps) : null;
        AppTree.Visibility = showFolders ? Visibility.Visible : Visibility.Collapsed;
        AppList.Visibility = showFolders ? Visibility.Collapsed : Visibility.Visible;
        AppList.ItemsSource = showFolders ? null : results;
        ResultsHeading.Text = query.Length > 0 ? "Search results" : showFolders ? "Programs" : "All apps";
        ResultCount.Text = results.Count.ToString();
        EmptyMessage.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!showFolders && results.Count > 0 && AppList.SelectedIndex < 0) AppList.SelectedIndex = 0;
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
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin && AppTree.Visibility == Visibility.Visible && AppTree.Items.Count > 0)
        {
            AppTree.Focus();
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

    private void AppTree_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelected();

    private void LaunchSelected()
    {
        var entry = AppTree.Visibility == Visibility.Visible
            ? (AppTree.SelectedItem as StartMenuNode)?.Application
            : AppList.SelectedItem as AppEntry;
        if (entry is null) return;
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
