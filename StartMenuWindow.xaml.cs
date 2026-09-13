using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopTuner;

public partial class StartMenuWindow : Window
{
    private const string PinnedStartDragFormat = "DesktopTuner.StartPinnedApp";
    private readonly AppCatalogService _catalog = new();
    private readonly Func<IReadOnlyList<AppEntry>, bool>? _savePinnedApps;
    private IReadOnlyList<AppEntry> _apps = [];
    private IReadOnlyList<AppEntry> _pinnedApps = [];
    private StartMenuStyle _style = StartMenuStyle.Modern;
    private bool _catalogLoaded;
    private string? _catalogLoadError;
    private string? _pinnedStartDragCandidate;
    private Point _pinnedStartDrag;
    private bool _suppressPinnedStartClick;

    public StartMenuWindow(StartMenuStyle style, IEnumerable<AppEntry>? pinnedApps = null, Func<IReadOnlyList<AppEntry>, bool>? savePinnedApps = null)
    {
        InitializeComponent();
        _pinnedApps = StartPinCatalog.Normalize(pinnedApps);
        _savePinnedApps = savePinnedApps;
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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
        RefreshApps();
        try
        {
            _apps = await LoadAppCatalogOnStaThreadAsync();
        }
        catch (Exception ex)
        {
            _catalogLoadError = $"Could not load installed apps: {ex.Message}";
        }
        finally
        {
            _catalogLoaded = true;
            if (IsLoaded) RefreshApps();
        }
    }

    private Task<IReadOnlyList<AppEntry>> LoadAppCatalogOnStaThreadAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<AppEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(_catalog.FindStartMenuApps());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Desktop Tuner Start app catalog"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshApps();

    private void RefreshApps()
    {
        var query = SearchBox?.Text.Trim() ?? string.Empty;
        PinnedStartItems.ItemsSource = _pinnedApps;
        PinnedStartPanel.Visibility = query.Length == 0 && _pinnedApps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_catalogLoaded)
        {
            AppTree.ItemsSource = null;
            AppTree.Visibility = Visibility.Collapsed;
            AppList.ItemsSource = null;
            AppList.Visibility = Visibility.Collapsed;
            ResultsHeading.Text = query.Length == 0 ? "Loading apps" : "Searching apps";
            SearchActionPanel.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultCount.Text = "…";
            EmptyMessage.Text = query.Length == 0 ? "Loading installed apps…" : "Searching installed apps…";
            EmptyMessage.Visibility = Visibility.Visible;
            return;
        }

        var results = _catalogLoadError is null ? AppCatalogService.Search(_apps, query) : Array.Empty<AppEntry>();
        var showFolders = _style == StartMenuStyle.Classic && query.Length == 0;
        AppTree.ItemsSource = showFolders ? AppCatalogService.BuildTree(_apps) : null;
        AppTree.Visibility = showFolders ? Visibility.Visible : Visibility.Collapsed;
        AppList.Visibility = showFolders ? Visibility.Collapsed : Visibility.Visible;
        AppList.ItemsSource = showFolders ? null : results;
        AppList.SelectedIndex = showFolders || results.Count == 0 ? -1 : 0;
        ResultsHeading.Text = query.Length > 0 ? "Search results" : showFolders ? "Programs" : "All apps";
        SearchActionPanel.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultCount.Text = results.Count.ToString();
        EmptyMessage.Text = _catalogLoadError ?? (query.Length > 0
            ? "No matching apps. Try a different search."
            : "No installed apps were found.");
        EmptyMessage.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            if (!_catalogLoaded)
            {
                e.Handled = true;
                return;
            }
            if (AppTree.SelectedItem is null && AppList.SelectedItem is null && !string.IsNullOrWhiteSpace(SearchBox.Text))
                OpenSearch(StartSearchTargetBuilder.WindowsSearch(SearchBox.Text), "Windows Search");
            else
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
        LaunchEntry(entry);
    }

    private void LaunchEntry(AppEntry entry)
    {
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

    private void RunAsAdministrator_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry { CanRunElevated: true } app }) return;
        try
        {
            AppCatalogService.LaunchAsAdministrator(app);
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user dismissed the UAC prompt.
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not run {app.Name} as administrator.\n\n{ex.Message}", "Could not elevate app", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry { CanOpenFileLocation: true } app }) return;
        try
        {
            AppCatalogService.OpenFileLocation(app);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not show the location for {app.Name}.\n\n{ex.Message}", "Could not open file location", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PinStartApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app }) return;
        var updated = StartPinCatalog.Pin(_pinnedApps, app);
        if (updated.Count == _pinnedApps.Count)
        {
            if (!_pinnedApps.Any(pin => string.Equals(pin.ShortcutPath, app.ShortcutPath, StringComparison.OrdinalIgnoreCase)))
                MessageBox.Show(this, $"You can pin up to {StartPinCatalog.MaximumPins} apps to Start.", "Start is full", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SavePinnedApps(updated);
    }

    private void PinnedStartApp_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressPinnedStartClick)
        {
            _suppressPinnedStartClick = false;
            e.Handled = true;
            return;
        }
        if (sender is Button { Tag: AppEntry app }) LaunchEntry(app);
    }

    private void PinnedStartApp_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: AppEntry app } button) return;
        _suppressPinnedStartClick = false;
        _pinnedStartDragCandidate = app.ShortcutPath;
        _pinnedStartDrag = e.GetPosition(button);
    }

    private void PinnedStartApp_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: AppEntry app }
            && string.Equals(app.ShortcutPath, _pinnedStartDragCandidate, StringComparison.OrdinalIgnoreCase))
            _pinnedStartDragCandidate = null;
    }

    private void PinnedStartApp_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button { Tag: AppEntry app } button
            || !string.Equals(app.ShortcutPath, _pinnedStartDragCandidate, StringComparison.OrdinalIgnoreCase)) return;
        var current = e.GetPosition(button);
        if (Math.Abs(current.X - _pinnedStartDrag.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _pinnedStartDrag.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _pinnedStartDragCandidate = null;
        var data = new DataObject(PinnedStartDragFormat, app.ShortcutPath);
        DragDrop.DoDragDrop(button, data, DragDropEffects.Move);
        _suppressPinnedStartClick = true;
    }

    private void PinnedStartApp_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(PinnedStartDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PinnedStartApp_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(PinnedStartDragFormat)
            || e.Data.GetData(PinnedStartDragFormat) is not string sourcePath
            || sender is not Button { Tag: AppEntry targetApp } target) return;

        var targetIndex = _pinnedApps.ToList().FindIndex(app =>
            string.Equals(app.ShortcutPath, targetApp.ShortcutPath, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0) return;
        var insertionIndex = targetIndex + (e.GetPosition(target).X >= target.ActualWidth / 2 ? 1 : 0);
        SavePinnedApps(StartPinCatalog.Reorder(_pinnedApps, sourcePath, insertionIndex));
        e.Handled = true;
    }

    private void UnpinStartApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app }) return;
        SavePinnedApps(StartPinCatalog.Unpin(_pinnedApps, app.ShortcutPath));
    }

    private void MoveStartAppEarlier_Click(object sender, RoutedEventArgs e) => MovePinnedStartApp(sender, -1);

    private void MoveStartAppLater_Click(object sender, RoutedEventArgs e) => MovePinnedStartApp(sender, 1);

    private void MovePinnedStartApp(object sender, int offset)
    {
        if (sender is not MenuItem { Tag: AppEntry app }) return;
        SavePinnedApps(StartPinCatalog.Move(_pinnedApps, app.ShortcutPath, offset));
    }

    private void SavePinnedApps(IReadOnlyList<AppEntry> apps)
    {
        if (_savePinnedApps?.Invoke(apps) == false) return;
        _pinnedApps = StartPinCatalog.Normalize(apps);
        RefreshApps();
    }

    private void QuickLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        if (action == "power")
        {
            if (sender is Button { ContextMenu: not null } powerButton)
            {
                var menu = powerButton.ContextMenu;
                if (menu.Items.Count == 0)
                {
                    foreach (var powerAction in StartPowerActionCatalog.Actions)
                    {
                        if (powerAction.Id == "sign-out") menu.Items.Add(new Separator());
                        var item = new MenuItem { Header = powerAction.Label, Tag = powerAction.Id };
                        item.Click += PowerAction_Click;
                        menu.Items.Add(item);
                    }
                }
                menu.PlacementTarget = powerButton;
                menu.Placement = PlacementMode.Top;
                menu.IsOpen = true;
            }
            return;
        }
        try
        {
            var target = action switch
            {
                "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "settings" => "ms-settings:",
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

    private void PowerAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string actionId }) return;
        var action = StartPowerActionCatalog.ById(actionId);
        if (action.RequiresConfirmation)
        {
            var choice = MessageBox.Show(this,
                $"Are you sure you want to {action.Label.ToLowerInvariant()}? Save your work in open apps first.",
                action.Label,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes) return;
        }

        try
        {
            StartPowerActionService.Execute(action.Id);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, $"Could not {action.Label.ToLowerInvariant()}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void WindowsSearch_Click(object sender, RoutedEventArgs e) =>
        OpenSearch(StartSearchTargetBuilder.WindowsSearch(SearchBox.Text), "Windows Search");

    private void WebSearch_Click(object sender, RoutedEventArgs e) =>
        OpenSearch(StartSearchTargetBuilder.WebSearch(SearchBox.Text), "web search");

    private void OpenSearch(string target, string description)
    {
        try
        {
            AppCatalogService.OpenLocation(target);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not open {description}.\n\n{ex.Message}", "Could not search", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MorePlaces_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: not null } button) return;
        var menu = button.ContextMenu;
        if (menu.Items.Count == 0)
        {
            foreach (var place in StartMenuPlaceCatalog.DropdownPlaces)
            {
                var item = new MenuItem { Header = place.Label, Tag = place.Id };
                item.Click += SystemPlace_Click;
                item.SubmenuOpened += PlaceFlyout_Opened;
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            foreach (var place in StartMenuPlaceCatalog.AdditionalPlaces)
            {
                var item = new MenuItem { Header = place.Label, Tag = place.Id };
                item.Click += SystemPlace_Click;
                menu.Items.Add(item);
            }
        }
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void PlaceFlyout_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string placeId } menuItem) return;
        FillPlaceFlyout(menuItem, StartMenuPlaceCatalog.ResolveTarget(placeId), depth: 0);
    }

    private void NestedPlaceFlyout_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: StartMenuPlaceEntry { IsDirectory: true } entry } menuItem) return;
        FillPlaceFlyout(menuItem, entry.FullPath, depth: 1);
    }

    private void FillPlaceFlyout(MenuItem menuItem, string directoryPath, int depth)
    {
        menuItem.Items.Clear();
        var entries = StartMenuPlaceCatalog.ReadChildren(directoryPath);
        if (entries.Count == 0)
        {
            menuItem.Items.Add(new MenuItem { Header = "No items", IsEnabled = false });
            return;
        }

        foreach (var entry in entries)
        {
            var item = new MenuItem { Header = entry.Name, Tag = entry };
            item.Click += StartPlaceEntry_Click;
            if (entry.IsDirectory && !entry.IsReparsePoint && depth < 1)
                item.SubmenuOpened += NestedPlaceFlyout_Opened;
            menuItem.Items.Add(item);
        }
    }

    private void StartPlaceEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: StartMenuPlaceEntry entry }) return;
        try
        {
            AppCatalogService.OpenLocation(entry.FullPath);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not open {entry.Name}.\n\n{ex.Message}", "Could not open Start place item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SystemPlace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string action }) return;
        try
        {
            if (action == "run")
            {
                if (!SystemFlyoutService.OpenRunDialog())
                    throw new InvalidOperationException("Windows did not accept the Run shortcut.");
                Close();
                return;
            }

            AppCatalogService.OpenLocation(StartMenuPlaceCatalog.ResolveTarget(action));
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open system place", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
