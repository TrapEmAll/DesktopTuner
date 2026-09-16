using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.VisualBasic;

namespace DesktopTuner;

public partial class StartMenuWindow : Window
{
    private const string PinnedStartDragFormat = "DesktopTuner.StartPinnedApp";
    private readonly AppCatalogService _catalog = new();
    private readonly StartRecentAppsStore _recentAppsStore;
    private readonly StartRecentFilesStore _recentFilesStore;
    private readonly Action? _exitShellHost;
    private readonly StartMenuIdentity _identity;
    private readonly Func<IReadOnlyList<AppEntry>, bool>? _savePinnedApps;
    private readonly Func<AppEntry, bool>? _pinTaskbarItem;
    private readonly Func<string, bool>? _openShellLocation;
    private readonly Func<string, bool>? _openFileLocation;
    private StartMenuPlacePreferences _startPlaces;
    private ControlPanelAppletPreferences _controlPanelApplets;
    private IReadOnlyList<AppEntry> _apps = [];
    private IReadOnlyList<AppEntry> _pinnedApps = [];
    private StartMenuStyle _style = StartMenuStyle.Modern;
    private int _recentAppCount;
    private bool _openAllApps;
    private bool _catalogLoaded;
    private string? _catalogLoadError;
    private string? _pinnedStartDragCandidate;
    private string? _appListDragCandidate;
    private Point _appListDrag;
    private Point _pinnedStartDrag;
    private bool _suppressPinnedStartClick;

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(StartMenuIconSize), typeof(StartMenuWindow), new PropertyMetadata(StartMenuIconSize.Standard));

    public StartMenuIconSize IconSize
    {
        get => (StartMenuIconSize)GetValue(IconSizeProperty);
        private set => SetValue(IconSizeProperty, value);
    }

    public StartMenuWindow(StartMenuStyle style, IEnumerable<AppEntry>? pinnedApps = null, Func<IReadOnlyList<AppEntry>, bool>? savePinnedApps = null, StartRecentAppsStore? recentAppsStore = null, StartMenuPlacePreferences? startPlaces = null, int recentAppCount = 4, ControlPanelAppletPreferences? controlPanelApplets = null, StartMenuIconSize iconSize = StartMenuIconSize.Standard, bool openAllApps = false, Func<string, bool>? openShellLocation = null, Func<string, bool>? openFileLocation = null, Func<AppEntry, bool>? pinTaskbarItem = null, Action? exitShellHost = null)
    {
        InitializeComponent();
        SourceInitialized += Window_SourceInitialized;
        _identity = StartMenuIdentityService.ReadCurrentUser();
        ProfileInitials.Text = _identity.Initials;
        if (_identity.PicturePath is { } picturePath)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(picturePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                ProfileAvatar.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                ProfileInitials.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) when (ex is IOException or UriFormatException or ArgumentException or NotSupportedException or InvalidOperationException or System.Security.SecurityException)
            {
                // Keep the initials avatar when the saved account picture is missing or unreadable.
                Trace.TraceWarning($"Could not display the local Windows account picture: {ex.Message}");
            }
        }
        _pinnedApps = StartPinCatalog.Normalize(pinnedApps);
        _savePinnedApps = savePinnedApps;
        _openShellLocation = openShellLocation;
        _openFileLocation = openFileLocation;
        _pinTaskbarItem = pinTaskbarItem;
        _exitShellHost = exitShellHost;
        _recentAppsStore = recentAppsStore ?? new StartRecentAppsStore();
        _recentFilesStore = new StartRecentFilesStore();
        _startPlaces = StartMenuPlaceCatalog.Normalize(startPlaces);
        _controlPanelApplets = ControlPanelAppletCatalog.Normalize(controlPanelApplets);
        _recentAppCount = Math.Clamp(recentAppCount, 0, StartRecentAppsStore.MaximumEntries);
        _openAllApps = openAllApps;
        SetIconSize(iconSize);
        SetStyle(style);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        SystemBackdropService.TryApplySmallRoundedCorners(handle);
        SystemBackdropService.TryApplyMica(this);
    }

    public void SetStartPlaces(StartMenuPlacePreferences preferences) => _startPlaces = StartMenuPlaceCatalog.Normalize(preferences);

    public void SetControlPanelApplets(ControlPanelAppletPreferences preferences) => _controlPanelApplets = ControlPanelAppletCatalog.Normalize(preferences);

    public void SetRecentAppCount(int count)
    {
        _recentAppCount = Math.Clamp(count, 0, StartRecentAppsStore.MaximumEntries);
        RefreshApps();
    }

    public void SetIconSize(StartMenuIconSize size) => IconSize = Enum.IsDefined(size) ? size : StartMenuIconSize.Standard;

    public void SetOpenAllApps(bool openAllApps)
    {
        _openAllApps = openAllApps;
        if (IsLoaded) RefreshApps();
    }

    public void FocusSearch(string query)
    {
        SearchBox.Text = query ?? string.Empty;
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    public void SetStyle(StartMenuStyle style)
    {
        _style = style;
        Tag = style == StartMenuStyle.Windows10 ? StartMenuStyle.Windows8 : style;
        MenuLayout.RowDefinitions.Clear();
        MenuLayout.ColumnDefinitions.Clear();
        var windows7 = style == StartMenuStyle.Windows7;
        var windows8 = style == StartMenuStyle.Windows8;
        var windows10 = style == StartMenuStyle.Windows10;
        var tileGrid = windows8 || windows10;
        var classic = style is StartMenuStyle.Classic or StartMenuStyle.Windows7;
        AppPanel.RowDefinitions.Clear();
        AppPanel.ColumnDefinitions.Clear();
        if (windows10)
        {
            AppPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AppPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AppPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            AppPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AppPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            Grid.SetColumnSpan(AppPanelHeading, 2);
            Grid.SetRow(RecentStartPanel, 1);
            Grid.SetColumn(RecentStartPanel, 0);
            Grid.SetRow(AppList, 2);
            Grid.SetColumn(AppList, 0);
            Grid.SetRow(AppTree, 2);
            Grid.SetColumn(AppTree, 0);
            Grid.SetRow(PinnedStartPanel, 1);
            Grid.SetColumn(PinnedStartPanel, 1);
            Grid.SetRowSpan(PinnedStartPanel, 2);
        }
        else
        {
            foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
                AppPanel.RowDefinitions.Add(new RowDefinition { Height = height });
            AppPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumnSpan(AppPanelHeading, 1);
            Grid.SetRow(PinnedStartPanel, 1);
            Grid.SetColumn(PinnedStartPanel, 0);
            Grid.SetRowSpan(PinnedStartPanel, 1);
            Grid.SetRow(RecentStartPanel, 2);
            Grid.SetColumn(RecentStartPanel, 0);
            Grid.SetRow(AppList, 3);
            Grid.SetColumn(AppList, 0);
            Grid.SetRow(AppTree, 2);
            Grid.SetColumn(AppTree, 0);
        }
        PinnedStartScrollViewer.Height = windows10 ? 360 : windows8 ? 220 : 82;
        PinnedStartScrollViewer.HorizontalScrollBarVisibility = tileGrid ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PinnedStartScrollViewer.VerticalScrollBarVisibility = tileGrid ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        PinnedStartItems.ItemsPanel = (ItemsPanelTemplate)FindResource(tileGrid ? "PinnedStartGroupsPanel" : "PinnedStartHorizontalPanel");
        MenuLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (classic) MenuLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });

        if (classic)
        {
            MenuLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (windows7)
            {
                MenuLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                MenuLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Width = 650;
                Height = 610;
                OuterBorder.CornerRadius = new CornerRadius(8);
                OuterBorder.BorderBrush = Brush("#7389A4");
                OuterBorder.Background = Brush("#E7ECF3");
                OuterBorder.Padding = new Thickness(0);
            }
            else
            {
                MenuLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                MenuLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Width = 650;
                Height = 590;
                OuterBorder.CornerRadius = new CornerRadius(8);
                OuterBorder.BorderBrush = Brush("#30394D");
                OuterBorder.Background = Brush("#F0F2F7");
                OuterBorder.Padding = new Thickness(20);
            }
            HeaderTitle.Text = _identity.DisplayName;
            HeaderSubtitle.Text = windows7 ? "Desktop Tuner" : "Search and launch apps";
            HeaderSubtitle.Visibility = Visibility.Visible;
            HeaderPanel.Margin = windows7 ? new Thickness(10, 6, 12, 4) : new Thickness(2, 0, 12, 12);
            SearchBox.Height = windows7 ? 38 : 42;
            SearchBox.Margin = windows7 ? new Thickness(8, 2, 8, 8) : new Thickness(0);
            AppPanel.Margin = windows7 ? new Thickness(0, 4, 8, 4) : new Thickness(0, 10, 12, 0);
            ResultsHeading.Text = "All programs";
            Grid.SetRow(HeaderPanel, 0);
            Grid.SetColumn(HeaderPanel, 0);
            Grid.SetRow(SearchBox, windows7 ? 2 : 1);
            Grid.SetColumn(SearchBox, 0);
            Grid.SetRow(AppPanel, windows7 ? 1 : 2);
            Grid.SetColumn(AppPanel, 0);
            Grid.SetRow(QuickLinksBorder, 0);
            Grid.SetColumn(QuickLinksBorder, 1);
            Grid.SetRowSpan(QuickLinksBorder, 3);
            QuickLinksBorder.Background = Brush(windows7 ? "#294B70" : "#252C3D");
            QuickLinksBorder.BorderBrush = Brush(windows7 ? "#294B70" : "#252C3D");
            QuickLinksBorder.BorderThickness = new Thickness(0);
            QuickLinksBorder.Padding = windows7 ? new Thickness(12, 12, 9, 12) : new Thickness(14, 14, 10, 14);
            QuickLinksStack.Orientation = Orientation.Vertical;
            QuickLinksTitle.Visibility = Visibility.Visible;
            SetQuickLinkAppearance(classic: true);
        }
        else
        {
            OuterBorder.Padding = new Thickness(20);
            SearchBox.Margin = new Thickness(0);
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
                Width = windows10 ? 820 : windows8 ? 700 : 470;
                Height = windows10 ? 700 : windows8 ? 740 : 650;
                OuterBorder.CornerRadius = new CornerRadius(tileGrid ? 8 : 18);
                OuterBorder.BorderBrush = tileGrid ? Brush("#27486A") : Brush("#DDE2EB");
                OuterBorder.Background = tileGrid ? Brush("#F1F4F8") : Brush("#F9FAFD");
                if (tileGrid)
                {
                    OuterBorder.SetResourceReference(Border.BorderBrushProperty, "DesktopBorderBrush");
                    OuterBorder.SetResourceReference(Border.BackgroundProperty, "DesktopWindowBrush");
                }
                HeaderTitle.Text = tileGrid ? "Start" : "Good to see you";
                HeaderSubtitle.Text = windows10 ? "Apps and pinned tiles" : windows8 ? "Pinned tiles and all apps" : "Search apps or open a favorite place";
                HeaderSubtitle.Visibility = Visibility.Visible;
                HeaderPanel.Margin = new Thickness(2, 0, 0, tileGrid ? 10 : 18);
                SearchBox.Height = tileGrid ? 42 : 46;
                SearchBox.FontSize = 14;
                AppPanel.Margin = new Thickness(0, tileGrid ? 10 : 16, 0, 10);
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
            if (IsLoaded)
            {
                RefreshApps();
                if (_openAllApps)
                {
                    _ = Dispatcher.BeginInvoke(new Action(FocusAllApps), DispatcherPriority.Input);
                }
            }
        }
    }

    private void FocusAllApps()
    {
        var target = AppTree.Visibility == Visibility.Visible ? (UIElement)AppTree : AppList;
        target.Focus();
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
        if (_style is StartMenuStyle.Windows8 or StartMenuStyle.Windows10)
        {
            var groupedPins = new ListCollectionView(_pinnedApps.ToList());
            groupedPins.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppEntry.GroupName)));
            PinnedStartItems.ItemsSource = groupedPins;
        }
        else
        {
            PinnedStartItems.ItemsSource = _pinnedApps;
        }
        var showOverview = StartMenuOpenModePolicy.ShouldShowOverview(query, _openAllApps);
        var showPinnedPanel = showOverview;
        PinnedStartPanel.Visibility = showPinnedPanel ? Visibility.Visible : Visibility.Collapsed;
        PinnedStartEmptyHint.Visibility = showPinnedPanel && _pinnedApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PinnedStartScrollViewer.Visibility = _pinnedApps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentStartPanel.Visibility = showOverview ? Visibility.Visible : Visibility.Collapsed;
        if (!_catalogLoaded)
        {
            RecentStartPanel.Visibility = Visibility.Collapsed;
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

        var recentFiles = query.Length == 0
            ? Array.Empty<AppEntry>()
            : _recentFilesStore.ReadRecentFiles(_apps.Select(app => app.ShortcutPath));
        var searchableEntries = query.Length == 0
            ? _apps
            : _apps.Concat(recentFiles);
        var results = _catalogLoadError is null ? AppCatalogService.Search(searchableEntries, query) : Array.Empty<AppEntry>();
        var recentItems = query.Length == 0
            ? _recentAppsStore.Resolve(_apps)
                .Where(app => !_pinnedApps.Any(pinned => string.Equals(pinned.ShortcutPath, app.ShortcutPath, StringComparison.OrdinalIgnoreCase)))
                .Concat(_recentFilesStore.ReadRecentFiles(_apps.Select(app => app.ShortcutPath)))
                .Take(_recentAppCount)
                .ToList()
            : [];
        RecentStartItems.ItemsSource = recentItems;
        RecentStartPanel.Visibility = recentItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var showFolders = StartMenuAppNavigationPolicy.ShouldShowProgramFolders(_style, query);
        AppTree.ItemsSource = showFolders ? AppCatalogService.BuildTree(_apps) : null;
        AppTree.Visibility = showFolders ? Visibility.Visible : Visibility.Collapsed;
        AppList.Visibility = showFolders ? Visibility.Collapsed : Visibility.Visible;
        AppList.ItemsSource = showFolders
            ? null
            : query.Length == 0
                ? StartMenuAppListPolicy.AddAlphabetMarkers(results)
                : results.Select(application => new StartMenuAppListItem(application, null)).ToList();
        Grid.SetColumnSpan(AppList, _style == StartMenuStyle.Windows10 && query.Length > 0 ? 2 : 1);
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

    private void AppList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _appListDragCandidate = null;
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(AppList, source) is not ListBoxItem { DataContext: StartMenuAppListItem item }
            || !StartPinCatalog.IsSupported(item.Application)) return;
        _appListDragCandidate = item.Application.ShortcutPath;
        _appListDrag = e.GetPosition(AppList);
    }

    private void AppList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _appListDragCandidate is null) return;
        var current = e.GetPosition(AppList);
        if (Math.Abs(current.X - _appListDrag.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _appListDrag.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var shortcutPath = _appListDragCandidate;
        _appListDragCandidate = null;
        DragDrop.DoDragDrop(AppList, new DataObject(PinnedStartDragFormat, shortcutPath), DragDropEffects.Move);
    }

    private void AppTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _appListDragCandidate = null;
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(AppTree, source) is not TreeViewItem { DataContext: StartMenuNode { Application: { } app } }
            || !StartPinCatalog.IsSupported(app)) return;
        _appListDragCandidate = app.ShortcutPath;
        _appListDrag = e.GetPosition(AppTree);
    }

    private void AppTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _appListDragCandidate is null) return;
        var current = e.GetPosition(AppTree);
        if (Math.Abs(current.X - _appListDrag.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _appListDrag.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var shortcutPath = _appListDragCandidate;
        _appListDragCandidate = null;
        DragDrop.DoDragDrop(AppTree, new DataObject(PinnedStartDragFormat, shortcutPath), DragDropEffects.Move);
    }

    private void AppTree_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelected();

    private void LaunchSelected()
    {
        var entry = AppTree.Visibility == Visibility.Visible
            ? (AppTree.SelectedItem as StartMenuNode)?.Application
            : AppList.SelectedItem switch
            {
                StartMenuAppListItem item => item.Application,
                AppEntry app => app,
                _ => null
            };
        if (entry is null) return;
        LaunchEntry(entry);
    }

    private void LaunchEntry(AppEntry entry)
    {
        try
        {
            if ((entry.IsDirectory || entry.IsShellNamespace) && _openShellLocation?.Invoke(entry.ShortcutPath) == true)
            {
                Close();
                return;
            }
            AppCatalogService.Launch(entry);
            if (!entry.IsDirectory && !_recentAppsStore.TryRecordLaunch(entry))
            {
                MessageBox.Show(this, "The app opened, but Desktop Tuner could not save recently used app history.", "Recent app history unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not open {entry.Name}.\n\n{ex.Message}", "Could not launch app", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RecentStartApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AppEntry app }) LaunchEntry(app);
    }

    private void RemoveRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app }) return;
        var removed = _recentFilesStore.IsRecentShortcut(app.ShortcutPath)
            ? _recentFilesStore.TryRemove(app.ShortcutPath)
            : _recentAppsStore.TryRemove(app.ShortcutPath);
        if (!removed)
        {
            MessageBox.Show(this, $"Desktop Tuner could not remove {app.Name} from recent items.", "Recent item unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RefreshApps();
    }

    private void RecentStartContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { DataContext: AppEntry app } menu) return;
        var pinStartItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Pin to Start"));
        var pinTaskbarItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Pin to taskbar"));
        var runAsAdministratorItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Run as administrator"));
        var hasRecentFileTarget = _recentFilesStore.TryResolveTargetPath(app.ShortcutPath, out var recentFileTarget)
            && File.Exists(recentFileTarget);
        var openWithItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Open with…"));
        var printItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Print"));
        if (openWithItem is not null)
        {
            openWithItem.Visibility = hasRecentFileTarget ? Visibility.Visible : Visibility.Collapsed;
            openWithItem.IsEnabled = hasRecentFileTarget;
        }
        if (printItem is not null)
        {
            printItem.Visibility = hasRecentFileTarget ? Visibility.Visible : Visibility.Collapsed;
            printItem.IsEnabled = hasRecentFileTarget;
        }
        if (pinStartItem is not null)
            pinStartItem.IsEnabled = StartPinCatalog.IsSupported(app)
                && !_pinnedApps.Any(pin => string.Equals(pin.ShortcutPath, app.ShortcutPath, StringComparison.OrdinalIgnoreCase));
        if (pinTaskbarItem is not null)
            pinTaskbarItem.IsEnabled = _pinTaskbarItem is not null && app.CanPinToTaskbar;
        if (runAsAdministratorItem is not null)
            runAsAdministratorItem.IsEnabled = !_recentFilesStore.IsRecentShortcut(app.ShortcutPath) && app.CanRunElevated;
        ConfigureNativeShellMenu(menu, app);
    }

    private void StartApplicationContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var app = menu.DataContext switch
        {
            StartMenuAppListItem item => item.Application,
            StartMenuNode node => node.Application,
            _ => null
        };
        if (app is not null) ConfigureNativeShellMenu(menu, app);
    }

    private void ConfigureNativeShellMenu(ContextMenu menu, AppEntry app)
    {
        var item = menu.Items.OfType<MenuItem>().FirstOrDefault(candidate => Equals(candidate.Header, "Show more options"));
        if (item is null) return;
        var target = _recentFilesStore.TryResolveTargetPath(app.ShortcutPath, out var recentTarget)
            && File.Exists(recentTarget)
            ? recentTarget
            : GetNativeShellTarget(app);
        item.Tag = target;
        item.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string? GetNativeShellTarget(AppEntry app)
    {
        if (app.IsPackagedApp && TaskbarPinCatalog.IsSupportedPackagedTarget(app.ShortcutPath))
            return $"shell:AppsFolder\\{app.ShortcutPath}";
        if (app.IsShellNamespace) return app.ShortcutPath;
        return File.Exists(app.ShortcutPath) || Directory.Exists(app.ShortcutPath) ? app.ShortcutPath : null;
    }

    private void ClearRecentStartApps_Click(object sender, RoutedEventArgs e)
    {
        if (!_recentAppsStore.TryClear())
        {
            MessageBox.Show(this, "Desktop Tuner could not clear recently used app history.", "Recent app history unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RefreshApps();
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

    private async void OpenWithRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app } || !_recentFilesStore.TryResolveTargetPath(app.ShortcutPath, out var targetPath) || !File.Exists(targetPath)) return;
        try
        {
            var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.OpenWithShellItemAsync(owner, targetPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not open the Open with dialog.\n\n{ex.Message}", "Could not open the Open with dialog", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PrintRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app } || !_recentFilesStore.TryResolveTargetPath(app.ShortcutPath, out var targetPath) || !File.Exists(targetPath)) return;
        try
        {
            var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.PrintShellItemAsync(owner, targetPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not print the selected file.\n\n{ex.Message}", "Could not print the selected file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry { CanOpenFileLocation: true } app }) return;
        try
        {
            var locationPath = app.ShortcutPath;
            var resolvedRecentTarget = _recentFilesStore.TryResolveTargetPath(app.ShortcutPath, out var recentTargetPath)
                && (File.Exists(recentTargetPath) || Directory.Exists(recentTargetPath));
            if (resolvedRecentTarget) locationPath = recentTargetPath;
            if (_openFileLocation?.Invoke(locationPath) == true)
            {
                Close();
                return;
            }
            if (resolvedRecentTarget)
                AppCatalogService.OpenPathLocation(locationPath);
            else
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

    private void PinTaskbarItem_Click(object sender, RoutedEventArgs e)
    {
        if (_pinTaskbarItem is null || sender is not MenuItem { Tag: AppEntry app }) return;
        if (!_pinTaskbarItem(app))
            MessageBox.Show(this, $"Could not pin {app.Name} to the taskbar.", "Taskbar pin unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (e.Data.GetDataPresent(PinnedStartDragFormat))
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = HasDroppedStartEntries(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void PinnedStartApp_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button { Tag: AppEntry targetApp } target) return;

        var targetIndex = _pinnedApps.ToList().FindIndex(app =>
            string.Equals(app.ShortcutPath, targetApp.ShortcutPath, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0) return;
        var insertionIndex = targetIndex + (e.GetPosition(target).X >= target.ActualWidth / 2 ? 1 : 0);
        if (!e.Data.GetDataPresent(PinnedStartDragFormat))
        {
            InsertDroppedApps(GetDroppedAppPaths(e.Data), insertionIndex, targetApp.GroupName);
            InsertDroppedShellNamespaces(GetDroppedShellNamespacePaths(e.Data), insertionIndex, targetApp.GroupName);
            e.Handled = true;
            return;
        }
        if (e.Data.GetData(PinnedStartDragFormat) is not string sourcePath) return;

        if (_pinnedApps.Any(app => string.Equals(app.ShortcutPath, sourcePath, StringComparison.OrdinalIgnoreCase)))
        {
            var reordered = StartPinCatalog.Reorder(_pinnedApps, sourcePath, insertionIndex);
            SavePinnedApps(StartPinCatalog.SetGroup(reordered, sourcePath, targetApp.GroupName));
        }
        else if (FindApp(sourcePath) is { } app)
        {
            var updated = StartPinCatalog.Pin(_pinnedApps, app);
            updated = StartPinCatalog.SetGroup(updated, app.ShortcutPath, targetApp.GroupName);
            SavePinnedApps(StartPinCatalog.Reorder(updated, app.ShortcutPath, insertionIndex));
        }
        e.Handled = true;
    }

    private void PinnedStartPanel_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(PinnedStartDragFormat))
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = HasDroppedStartEntries(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void PinnedStartPanel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(PinnedStartDragFormat))
        {
            if (e.Data.GetData(PinnedStartDragFormat) is string shortcutPath)
            {
                if (_pinnedApps.Any(app => string.Equals(app.ShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase)))
                    SavePinnedApps(StartPinCatalog.Reorder(_pinnedApps, shortcutPath, _pinnedApps.Count));
                else if (FindApp(shortcutPath) is { } app)
                    InsertPinnedApp(app, _pinnedApps.Count);
            }
            e.Handled = true;
            return;
        }

        InsertDroppedApps(GetDroppedAppPaths(e.Data), _pinnedApps.Count);
        InsertDroppedShellNamespaces(GetDroppedShellNamespacePaths(e.Data), _pinnedApps.Count);
        e.Handled = true;
    }

    private bool HasDroppedStartEntries(IDataObject data) =>
        (data.GetDataPresent(DataFormats.FileDrop)
            && data.GetData(DataFormats.FileDrop) is string[] paths
            && StartPinCatalog.AddDroppedFiles([], paths.Where(path => File.Exists(path) || Directory.Exists(path))).Count > 0)
        || GetDroppedShellNamespacePaths(data).Any();

    private static IEnumerable<string> GetDroppedAppPaths(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(path => File.Exists(path) || Directory.Exists(path))
            : [];

    private static IEnumerable<string> GetDroppedShellNamespacePaths(IDataObject data) =>
        NativeShellContextMenuService.ReadShellDropParsingNames(data)
            .Where(TaskbarPinCatalog.IsSupportedShellNamespaceTarget);

    private void InsertDroppedApps(IEnumerable<string> paths, int index, string? groupName = null)
    {
        var droppedApps = StartPinCatalog.AddDroppedFiles([], paths)
            .Select(app => groupName is null ? app : app with { GroupName = StartPinCatalog.NormalizeGroupName(groupName) })
            .ToList();
        if (droppedApps.Count == 0) return;

        var updated = _pinnedApps;
        var insertionIndex = Math.Clamp(index, 0, updated.Count);
        var reachedPinLimit = false;
        foreach (var app in droppedApps)
        {
            if (updated.Any(pin => string.Equals(pin.ShortcutPath, app.ShortcutPath, StringComparison.OrdinalIgnoreCase))) continue;
            if (updated.Count >= StartPinCatalog.MaximumPins)
            {
                reachedPinLimit = true;
                break;
            }
            updated = StartPinCatalog.Pin(updated, app);
            updated = StartPinCatalog.Reorder(updated, app.ShortcutPath, insertionIndex++);
        }

        if (updated.Count == _pinnedApps.Count)
        {
            if (droppedApps.Any(app => !_pinnedApps.Any(pin => string.Equals(pin.ShortcutPath, app.ShortcutPath, StringComparison.OrdinalIgnoreCase))))
                ShowStartPinLimitMessage();
            return;
        }
        SavePinnedApps(updated);
        if (reachedPinLimit) ShowStartPinLimitMessage();
    }

    private void InsertDroppedShellNamespaces(IEnumerable<string> parsingNames, int index, string? groupName = null)
    {
        var droppedApps = parsingNames
            .Select(parsingName => new AppEntry(
                DesktopShellNamespaceCatalog.GetFriendlyName(parsingName),
                parsingName,
                GroupName: groupName is null ? StartPinCatalog.DefaultGroupName : StartPinCatalog.NormalizeGroupName(groupName),
                IsDirectory: true,
                IsShellNamespace: true))
            .GroupBy(app => app.ShortcutPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (droppedApps.Count == 0) return;

        var updated = _pinnedApps;
        var insertionIndex = Math.Clamp(index, 0, updated.Count);
        var reachedPinLimit = false;
        foreach (var app in droppedApps)
        {
            if (updated.Any(pin => string.Equals(pin.ShortcutPath, app.ShortcutPath, StringComparison.OrdinalIgnoreCase))) continue;
            if (updated.Count >= StartPinCatalog.MaximumPins)
            {
                reachedPinLimit = true;
                break;
            }
            updated = StartPinCatalog.Pin(updated, app);
            updated = StartPinCatalog.Reorder(updated, app.ShortcutPath, insertionIndex++);
        }

        if (updated.Count == _pinnedApps.Count)
        {
            if (droppedApps.Any(app => !_pinnedApps.Any(pin => string.Equals(pin.ShortcutPath, app.ShortcutPath, StringComparison.OrdinalIgnoreCase))))
                ShowStartPinLimitMessage();
            return;
        }
        SavePinnedApps(updated);
        if (reachedPinLimit) ShowStartPinLimitMessage();
    }

    private void ShowStartPinLimitMessage() =>
        MessageBox.Show(this, $"You can pin up to {StartPinCatalog.MaximumPins} apps and folders to Start.", "Start is full", MessageBoxButton.OK, MessageBoxImage.Information);

    private AppEntry? FindApp(string shortcutPath) => _apps.FirstOrDefault(app =>
        string.Equals(app.ShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase));

    private void InsertPinnedApp(AppEntry app, int index)
    {
        var updated = StartPinCatalog.Pin(_pinnedApps, app);
        if (updated.Count == _pinnedApps.Count)
        {
            if (!_pinnedApps.Any(pin => string.Equals(pin.ShortcutPath, app.ShortcutPath, StringComparison.OrdinalIgnoreCase)))
                MessageBox.Show(this, $"You can pin up to {StartPinCatalog.MaximumPins} apps and folders to Start.", "Start is full", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SavePinnedApps(StartPinCatalog.Reorder(updated, app.ShortcutPath, index));
    }

    private void UnpinStartApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app }) return;
        SavePinnedApps(StartPinCatalog.Unpin(_pinnedApps, app.ShortcutPath));
    }

    private void PinnedStartContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { DataContext: AppEntry app } menu) return;
        var openFolderItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Open folder"));
        var browseFolderItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Browse folder contents"));
        var openFileLocationItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Open file location"));
        if (openFolderItem is not null) openFolderItem.Visibility = app.IsDirectory || app.IsShellNamespace ? Visibility.Visible : Visibility.Collapsed;
        if (browseFolderItem is not null)
        {
            browseFolderItem.Visibility = app.IsDirectory || app.IsShellNamespace ? Visibility.Visible : Visibility.Collapsed;
            browseFolderItem.IsEnabled = app.IsDirectory || app.IsShellNamespace;
        }
        if (openFileLocationItem is not null) openFileLocationItem.Visibility = app.IsDirectory || app.IsShellNamespace ? Visibility.Collapsed : Visibility.Visible;
        ConfigureNativeShellMenu(menu, app);
        var tileSizeMenu = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Tile size"));
        var tileLayout = _style is StartMenuStyle.Windows8 or StartMenuStyle.Windows10;
        if (tileSizeMenu is not null)
        {
            tileSizeMenu.Visibility = tileLayout ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in tileSizeMenu.Items.OfType<MenuItem>())
                item.IsChecked = item.Tag is StartTileSize size && size == app.TileSize;
        }
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (Equals(item.Header, "Move to group") || Equals(item.Header, "Create group and move…") || Equals(item.Header, "Rename this group…"))
                item.Visibility = tileLayout ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OpenPinnedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: AppEntry app } && (app.IsDirectory || app.IsShellNamespace)) LaunchEntry(app);
    }

    private async void PinnedStartFolderMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app } menuItem
            || (!app.IsDirectory && !app.IsShellNamespace)) return;
        await FillPlaceFlyoutAsync(menuItem, app.ShortcutPath);
    }

    private async void ShowNativeStartShellContextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string parsingName }) return;
        try
        {
            var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (DesktopShellNamespaceCatalog.IsShellNamespaceLocation(parsingName))
                await NativeShellContextMenuService.ShowForShellItemAsync(owner, parsingName);
            else
                await NativeShellContextMenuService.ShowForItemsAsync(owner, [parsingName]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Windows could not show the native menu.\n\n{ex.Message}", "Could not open Shell menu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PinnedStartTileSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: StartTileSize size } item
            || ItemsControl.ItemsControlFromItemContainer(item) is not MenuItem { Tag: AppEntry app }) return;
        SavePinnedApps(StartPinCatalog.SetTileSize(_pinnedApps, app.ShortcutPath, size));
    }

    private void PinnedStartGroupMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app } menu) return;
        menu.Items.Clear();
        foreach (var group in StartPinCatalog.Group(_pinnedApps))
        {
            var item = new MenuItem { Header = group.Name, Tag = group.Name, IsCheckable = true, IsChecked = string.Equals(group.Name, app.GroupName, StringComparison.OrdinalIgnoreCase) };
            item.Click += MoveStartAppToGroup_Click;
            menu.Items.Add(item);
        }
    }

    private void MoveStartAppToGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string groupName } item
            || ItemsControl.ItemsControlFromItemContainer(item) is not MenuItem { Tag: AppEntry app }) return;
        MovePinnedStartAppToGroup(app.ShortcutPath, groupName);
    }

    private void CreateStartGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app }) return;
        var groupName = Interaction.InputBox("Enter a name for this Start tile group (up to 32 characters).", "Create Start group");
        if (!string.IsNullOrWhiteSpace(groupName)) MovePinnedStartAppToGroup(app.ShortcutPath, groupName);
    }

    private void RenameStartGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppEntry app }) return;
        var groupName = Interaction.InputBox("Enter a new name for this Start tile group (up to 32 characters).", "Rename Start group", app.GroupName);
        if (string.IsNullOrWhiteSpace(groupName) || string.Equals(groupName.Trim(), app.GroupName, StringComparison.OrdinalIgnoreCase)) return;
        SavePinnedApps(StartPinCatalog.RenameGroup(_pinnedApps, app.GroupName, groupName));
    }

    private void MovePinnedStartAppToGroup(string shortcutPath, string groupName)
    {
        groupName = StartPinCatalog.NormalizeGroupName(groupName);
        var insertionIndex = _pinnedApps.Select((app, index) => (app, index))
            .Where(item => string.Equals(item.app.GroupName, groupName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.app.ShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index + 1)
            .DefaultIfEmpty(_pinnedApps.Count)
            .Max();
        var updated = StartPinCatalog.SetGroup(_pinnedApps, shortcutPath, groupName);
        SavePinnedApps(StartPinCatalog.Reorder(updated, shortcutPath, insertionIndex));
    }

    private void PinnedStartGroup_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(PinnedStartDragFormat)
            ? DragDropEffects.Move
            : HasDroppedStartEntries(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void PinnedStartGroup_Drop(object sender, DragEventArgs e)
    {
        if (sender is not TextBlock { Tag: string groupName }) return;
        if (e.Data.GetDataPresent(PinnedStartDragFormat) && e.Data.GetData(PinnedStartDragFormat) is string shortcutPath)
        {
            MovePinnedStartAppToGroup(shortcutPath, groupName);
            e.Handled = true;
            return;
        }
        var insertionIndex = _pinnedApps.Select((app, index) => (app, index))
            .Where(item => string.Equals(item.app.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index + 1)
            .DefaultIfEmpty(_pinnedApps.Count)
            .Max();
        InsertDroppedApps(GetDroppedAppPaths(e.Data), insertionIndex, groupName);
        InsertDroppedShellNamespaces(GetDroppedShellNamespacePaths(e.Data), insertionIndex, groupName);
        e.Handled = true;
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
                    if (_exitShellHost is not null)
                    {
                        menu.Items.Add(new Separator());
                        var exitShell = new MenuItem { Header = "Exit shell replacement and start Explorer" };
                        exitShell.Click += (_, _) =>
                        {
                            Close();
                            _exitShellHost();
                        };
                        menu.Items.Add(exitShell);
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
        menu.Items.Clear();
        var placesById = StartMenuPlaceCatalog.AllPlaces.ToDictionary(place => place.Id, StringComparer.OrdinalIgnoreCase);
        var dropdownIds = StartMenuPlaceCatalog.DropdownPlaces.Select(place => place.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var firstAdditional = _startPlaces.Order!.FindIndex(id => !dropdownIds.Contains(id));
        var hasSeparatedSections = firstAdditional >= 0 && _startPlaces.Order.Take(firstAdditional).All(dropdownIds.Contains)
            && _startPlaces.Order.Skip(firstAdditional).All(id => !dropdownIds.Contains(id));
        var visibleIds = _startPlaces.Visible!.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _startPlaces.Order.Count; index++)
        {
            var placeId = _startPlaces.Order[index];
            if (!visibleIds.Contains(placeId)) continue;
            if (hasSeparatedSections && index == firstAdditional && menu.Items.Count > 0) menu.Items.Add(new Separator());
            var place = placesById[placeId];
            var iconTarget = TryResolveStartPlaceIconTarget(placeId);
            var icon = iconTarget is null ? null
                : DesktopShellNamespaceCatalog.IsShellNamespaceLocation(iconTarget)
                    ? TaskbarIconService.LoadNamespaceIcon(iconTarget)
                    : TaskbarIconService.LoadIcon(iconTarget);
            var item = new MenuItem
            {
                Header = place.Label,
                Tag = place.Id,
                Icon = icon is null ? null : new Image { Source = icon, Width = 18, Height = 18 }
            };
            item.Click += SystemPlace_Click;
            if (TryResolveNativeStartShellTarget(placeId) is { } nativeTarget)
                item.ContextMenu = CreateNativeStartShellContextMenu(nativeTarget);
            if (dropdownIds.Contains(placeId)) item.SubmenuOpened += PlaceFlyout_Opened;
            if (placeId == "control-panel") item.SubmenuOpened += ControlPanelFlyout_Opened;
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No places selected", IsEnabled = false });
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void PlaceFlyout_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string placeId } menuItem) return;
        await FillPlaceFlyoutAsync(menuItem, StartMenuPlaceCatalog.ResolveTarget(placeId));
    }

    private void ControlPanelFlyout_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        menuItem.Items.Clear();
        var openControlPanel = new MenuItem { Header = "Open Control Panel" };
        openControlPanel.Click += SystemPlace_Click;
        openControlPanel.Tag = "control-panel";
        menuItem.Items.Add(openControlPanel);
        menuItem.Items.Add(new Separator());

        var visibleApplets = _controlPanelApplets.Visible!.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableApplets = ControlPanelAppletCatalog.GetAvailableApplets(Environment.SystemDirectory, canonicalApplicationNames: ControlPanelAppletCatalog.DiscoverCanonicalApplicationNames())
            .ToDictionary(applet => applet.Id, StringComparer.OrdinalIgnoreCase);
        var appletCount = 0;
        foreach (var appletId in _controlPanelApplets.Order!)
        {
            if (!visibleApplets.Contains(appletId) || !availableApplets.TryGetValue(appletId, out var applet)) continue;
            var item = new MenuItem { Header = applet.Label, Tag = applet.Id };
            item.Click += ControlPanelApplet_Click;
            if (TryGetControlPanelAppletTarget(applet) is { } nativeTarget)
                item.ContextMenu = CreateNativeStartShellContextMenu(nativeTarget);
            menuItem.Items.Add(item);
            appletCount++;
        }
        if (appletCount == 0)
            menuItem.Items.Add(new MenuItem { Header = "No Control Panel applets selected", IsEnabled = false });
    }

    private void ControlPanelApplet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string appletId }) return;
        try
        {
            System.Diagnostics.Process.Start(ControlPanelAppletCatalog.CreateStartInfo(appletId));
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not open this Control Panel applet.\n\n{ex.Message}", "Could not open Control Panel applet", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void NestedPlaceFlyout_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: StartMenuPlaceEntry { IsDirectory: true } entry } menuItem) return;
        await FillPlaceFlyoutAsync(menuItem, entry.FullPath);
    }

    private async Task FillPlaceFlyoutAsync(MenuItem menuItem, string directoryPath)
    {
        menuItem.Items.Clear();
        menuItem.Items.Add(new MenuItem { Header = "Loading…", IsEnabled = false });
        var entries = await StartMenuPlaceCatalog.ReadChildrenAsync(directoryPath);
        if (!menuItem.IsSubmenuOpen) return;
        menuItem.Items.Clear();
        if (entries.Count == 0)
        {
            menuItem.Items.Add(new MenuItem { Header = "No items", IsEnabled = false });
            return;
        }

        var icons = await Task.Run(() => entries.ToDictionary(
            entry => entry.FullPath,
            entry => DesktopShellNamespaceCatalog.IsShellNamespaceLocation(entry.FullPath)
                ? TaskbarIconService.LoadNamespaceIcon(entry.FullPath)
                : TaskbarIconService.LoadIcon(entry.FullPath),
            StringComparer.OrdinalIgnoreCase));
        if (!menuItem.IsSubmenuOpen) return;
        foreach (var entry in entries)
        {
            var icon = icons.GetValueOrDefault(entry.FullPath);
            var item = new MenuItem
            {
                Header = entry.Name,
                Tag = entry,
                Icon = icon is null
                    ? null
                    : new Image { Source = icon, Style = (Style)FindResource("StartFlyoutIcon") }
            };
            item.Click += StartPlaceEntry_Click;
            item.ContextMenu = CreateNativeStartShellContextMenu(entry.FullPath);
            if (StartMenuPlaceCatalog.CanExpand(entry))
                item.SubmenuOpened += NestedPlaceFlyout_Opened;
            menuItem.Items.Add(item);
        }
    }

    private static string? TryResolveNativeStartShellTarget(string placeId)
    {
        try
        {
            var target = StartMenuPlaceCatalog.ResolveTarget(placeId);
            return DesktopShellNamespaceCatalog.IsShellNamespaceLocation(target)
                || File.Exists(target)
                || Directory.Exists(target)
                ? target
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? TryResolveStartPlaceIconTarget(string placeId)
    {
        if (string.Equals(placeId, "control-panel", StringComparison.OrdinalIgnoreCase))
            return "shell:ControlPanelFolder";
        try
        {
            var target = StartMenuPlaceCatalog.ResolveTarget(placeId);
            return DesktopShellNamespaceCatalog.IsShellNamespaceLocation(target)
                || File.Exists(target)
                || Directory.Exists(target)
                ? target
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? TryGetControlPanelAppletTarget(ControlPanelApplet applet)
    {
        var target = applet.Arguments.FirstOrDefault(argument => argument.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase));
        if (target is null) return null;
        var path = Path.Combine(Environment.SystemDirectory, Path.GetFileName(target));
        return File.Exists(path) ? path : null;
    }

    private ContextMenu CreateNativeStartShellContextMenu(string target)
    {
        var nativeContextMenu = new ContextMenu();
        var nativeMenuItem = new MenuItem { Header = "Show more options", Tag = target };
        nativeMenuItem.Click += ShowNativeStartShellContextMenu_Click;
        nativeContextMenu.Items.Add(nativeMenuItem);
        return nativeContextMenu;
    }

    private void StartPlaceEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: StartMenuPlaceEntry entry }) return;
        try
        {
            if (entry.IsDirectory && _openShellLocation?.Invoke(entry.FullPath) == true)
            {
                Close();
                return;
            }
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
        if (action == "control-panel" && sender is MenuItem { HasItems: true }) return;
        try
        {
            if (action == "run")
            {
                if (!SystemFlyoutService.OpenRunDialog())
                    throw new InvalidOperationException("Windows did not accept the Run shortcut.");
                Close();
                return;
            }

            var target = StartMenuPlaceCatalog.ResolveTarget(action);
            var shellTarget = action switch
            {
                "control-panel" => "shell:ControlPanelFolder",
                "network" => "shell:NetworkPlacesFolder",
                _ => target
            };
            if (_openShellLocation?.Invoke(shellTarget) != true) AppCatalogService.OpenLocation(target);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open system place", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ProfileButton_Click(object sender, RoutedEventArgs e) => SystemFlyoutService.OpenAccountSettings();
}
