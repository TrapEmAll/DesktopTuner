using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopTuner;

public partial class ExplorerWindow : Window
{
    private const string ExplorerTabDragFormat = "DesktopTuner.ExplorerTabState";
    private sealed record ExplorerTabDragPayload(ExplorerWindow Source, ExplorerTabState Tab);
    private const string QuickAccessPinDragFormat = "DesktopTuner.ExplorerQuickAccessPin";
    private readonly CancellationTokenSource _navigationLoadCancellation = new();
    private readonly List<ExplorerTabState> _tabs = [];
    private readonly List<ExplorerTabState> _closedTabs = [];
    private readonly HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    private int _activeTabIndex;
    private bool _syncingTabs;
    private bool _updatingSortControls;
    private bool _updatingViewModeControl;
    private bool _updatingDriveGroupingControl;
    private bool _restoringFolderColumnWidths;
    private bool _hasCompletedInitialLayout;
    private int _columnWidthRestoreGeneration;
    private TabItem? _tabDragCandidate;
    private Point _tabDragStart;
    private ExplorerEntry? _entryDragCandidate;
    private Point _entryDragStart;
    private string? _quickAccessDragCandidate;
    private Point _quickAccessDragStart;
    private bool _suppressQuickAccessClick;
    private bool _syncingNavigationSelection;
    private int _navigationSyncGeneration;
    private IReadOnlyList<ExplorerNavigationNode> _navigationRoots = [];
    private ExplorerTabState ActiveTab => _tabs[_activeTabIndex];
    private List<ExplorerLocation> _back => ActiveTab.Back;
    private List<ExplorerLocation> _forward => ActiveTab.Forward;
    private ExplorerLocation _location { get => ActiveTab.Location; set => ActiveTab.Location = value; }
    private IReadOnlyList<ExplorerEntry> _entries { get => ActiveTab.Entries; set => ActiveTab.Entries = value; }
    private CancellationTokenSource? _searchCancellation { get => ActiveTab.SearchCancellation; set => ActiveTab.SearchCancellation = value; }
    private bool _isSearchView { get => ActiveTab.IsSearchView; set => ActiveTab.IsSearchView = value; }
    private readonly bool _showHiddenItems;
    private readonly bool _hideFileExtensions;
    private readonly bool _showRecentItems;
    private readonly ExplorerQuickAccessStore _quickAccessStore;
    private readonly ExplorerFolderViewStore _folderViewStore;
    private readonly ExplorerSessionStore _sessionStore;
    private readonly Func<string, bool>? _pinTaskbarItem;
    private readonly Func<string, bool>? _isTaskbarItemPinned;
    private ExplorerSortColumn _sortColumn
    {
        get => _tabs.Count == 0 ? ExplorerSortColumn.Name : _location.IsHome ? ActiveTab.HomeSortColumn : ActiveTab.SortColumn;
        set
        {
            if (_tabs.Count == 0) return;
            if (_location.IsHome) ActiveTab.HomeSortColumn = value;
            else ActiveTab.SortColumn = value;
        }
    }
    private bool _sortAscending
    {
        get => _tabs.Count == 0 || (_location.IsHome ? ActiveTab.HomeSortAscending : ActiveTab.SortAscending);
        set
        {
            if (_tabs.Count == 0) return;
            if (_location.IsHome) ActiveTab.HomeSortAscending = value;
            else ActiveTab.SortAscending = value;
        }
    }
    private double _detailsPaneHeight = 160;
    private bool _openFoldersInNewTab;

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        SystemBackdropService.TryApplyMica(this);
    }

    public ExplorerWindow(string? initialPath = null, bool showHiddenItems = false, bool hideFileExtensions = true, bool startInThisPc = false, bool showRecentItems = true, ExplorerQuickAccessStore? quickAccessStore = null, ExplorerFolderViewStore? folderViewStore = null, ExplorerSessionStore? sessionStore = null, bool restoreSavedSession = true, bool saveSession = true, bool? openFoldersInNewTab = null, Func<string, bool>? pinTaskbarItem = null, Func<string, bool>? isTaskbarItemPinned = null)
    {
        InitializeComponent();
        Loaded += (_, _) => _hasCompletedInitialLayout = true;
        _navigationRoots = CreateNavigationRoots();
        NavigationTree.ItemsSource = _navigationRoots;
        _showHiddenItems = showHiddenItems;
        _hideFileExtensions = hideFileExtensions;
        _showRecentItems = showRecentItems;
        _quickAccessStore = quickAccessStore ?? new ExplorerQuickAccessStore();
        _folderViewStore = folderViewStore ?? new ExplorerFolderViewStore();
        _sessionStore = sessionStore ?? new ExplorerSessionStore();
        _pinTaskbarItem = pinTaskbarItem;
        _isTaskbarItemPinned = isTaskbarItemPinned;
        RefreshQuickAccessPins();
        var savedSession = restoreSavedSession ? _sessionStore.Load() : null;
        _openFoldersInNewTab = openFoldersInNewTab ?? savedSession?.OpenFoldersInNewTab ?? false;
        OpenFoldersInNewTabToggle.IsChecked = _openFoldersInNewTab;
        var sessionToRestore = string.IsNullOrWhiteSpace(initialPath) && !startInThisPc ? savedSession : null;
        var initialLocation = sessionToRestore is not null
            ? sessionToRestore.Tabs[sessionToRestore.ActiveTabIndex].Location
            : startInThisPc && string.IsNullOrWhiteSpace(initialPath)
            ? new ExplorerLocation(null, IsDriveList: true)
                : string.IsNullOrWhiteSpace(initialPath)
                ? new ExplorerLocation(null, IsHome: true)
                : string.Equals(initialPath, "shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase) || string.Equals(initialPath, "This PC", StringComparison.OrdinalIgnoreCase)
                    ? new ExplorerLocation(null, IsDriveList: true)
                : IsRecycleBinAddress(initialPath)
                    ? new ExplorerLocation(null, IsRecycleBin: true)
                : Directory.Exists(initialPath)
                    ? new ExplorerLocation(Path.GetFullPath(initialPath))
                    : new ExplorerLocation(null, IsHome: true);
        if (sessionToRestore is null)
        {
            _tabs.Add(new ExplorerTabState(initialLocation));
            RestoreFolderViewPreferences(ActiveTab);
        }
        else
        {
            foreach (var savedTab in sessionToRestore.Tabs)
            {
                var tab = new ExplorerTabState(savedTab.Location)
                {
                    ViewMode = savedTab.ViewMode,
                    SortColumn = savedTab.SortColumn,
                    SortAscending = savedTab.SortAscending,
                    HomeSortColumn = savedTab.HomeSortColumn,
                    HomeSortAscending = savedTab.HomeSortAscending,
                    HomeSortExplicitly = savedTab.HomeSortExplicitly,
                    GroupDrives = savedTab.GroupDrives,
                    IsSearchView = !string.IsNullOrWhiteSpace(savedTab.Location.SearchQuery)
                };
                tab.Back.AddRange(savedTab.Back ?? []);
                tab.Forward.AddRange(savedTab.Forward ?? []);
                _tabs.Add(tab);
            }
            _activeTabIndex = sessionToRestore.ActiveTabIndex;
            _detailsPaneHeight = sessionToRestore.DetailsPaneHeight;
            DetailsPaneToggle.IsChecked = sessionToRestore.DetailsPaneVisible;
            SetDetailsPaneVisibility(sessionToRestore.DetailsPaneVisible, captureCurrentHeight: false);
        }
        RestoreColumnWidths(ActiveTab.Location.Path);
        UpdateSortPresentation();
        ApplyExplorerViewMode(ActiveTab.ViewMode);
        SyncExplorerTabs();
        Closed += (_, _) =>
        {
            if (saveSession) SaveExplorerSession();
            _navigationLoadCancellation.Cancel();
            _navigationLoadCancellation.Dispose();
            foreach (var tab in _tabs) CancelSearch(tab);
        };
        RefreshLocation();
    }

    public void OpenFolderFromShell(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"The folder no longer exists: {path}");
        AddTab(new ExplorerLocation(Path.GetFullPath(path)));
        Activate();
    }

    public void OpenShellLocationFromShell(string parsingName)
    {
        var location = parsingName.Trim().ToUpperInvariant() switch
        {
            "SHELL:MYCOMPUTERFOLDER" => new ExplorerLocation(null, IsDriveList: true),
            "SHELL:RECYCLEBINFOLDER" => new ExplorerLocation(null, IsRecycleBin: true),
            _ => throw new ArgumentException("That Shell location is not supported by Desktop Tuner Explorer.", nameof(parsingName))
        };
        AddTab(location);
        Activate();
    }

    private void SaveExplorerSession()
    {
        try
        {
            _sessionStore.Save(new ExplorerSession(_activeTabIndex, _tabs.Select(tab => new ExplorerTabSession(
                tab.Location,
                [.. tab.Back],
                [.. tab.Forward],
                tab.ViewMode,
                tab.SortColumn,
                tab.SortAscending,
                tab.HomeSortColumn,
                tab.HomeSortAscending,
                tab.HomeSortExplicitly,
                tab.GroupDrives)).ToList(), _detailsPaneHeight, DetailsPaneToggle.IsChecked == true, _openFoldersInNewTab));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Desktop Tuner could not save the open Explorer tabs. They will not be restored next time.\n\n{ex.Message}", "Explorer session not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SyncExplorerTabs()
    {
        _syncingTabs = true;
        try
        {
            ExplorerTabs.Items.Clear();
            foreach (var tab in _tabs)
            {
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                header.Children.Add(new TextBlock { Text = GetTabTitle(tab), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                var close = new Button { Content = "×", Padding = new Thickness(3, 0, 3, 0), MinWidth = 20, Height = 20, ToolTip = "Close tab" };
                close.Click += (_, _) => CloseTab(tab);
                header.Children.Add(close);
                var tabItem = new TabItem { Header = header, Tag = tab, Padding = new Thickness(8, 3, 8, 3), AllowDrop = true, ToolTip = "Drag to reorder or move this tab to another Explorer window" };
                tabItem.PreviewMouseLeftButtonDown += ExplorerTab_PreviewMouseLeftButtonDown;
                tabItem.PreviewMouseMove += ExplorerTab_PreviewMouseMove;
                tabItem.PreviewMouseLeftButtonUp += ExplorerTab_PreviewMouseLeftButtonUp;
                tabItem.PreviewMouseDown += ExplorerTab_PreviewMouseDown;
                tabItem.PreviewMouseRightButtonDown += ExplorerTab_PreviewMouseRightButtonDown;
                tabItem.DragOver += ExplorerTab_DragOver;
                tabItem.Drop += ExplorerTab_Drop;
                tabItem.ContextMenu = CreateTabContextMenu(tab);
                ExplorerTabs.Items.Add(tabItem);
            }
            ExplorerTabs.SelectedIndex = _activeTabIndex;
        }
        finally { _syncingTabs = false; }
    }

    private void UpdateExplorerTabTitles()
    {
        for (var index = 0; index < _tabs.Count && index < ExplorerTabs.Items.Count; index++)
        {
            if (ExplorerTabs.Items[index] is not TabItem { Header: StackPanel { Children: { Count: > 0 } } header }) continue;
            if (header.Children[0] is TextBlock title) title.Text = GetTabTitle(_tabs[index]);
        }
    }

    private static string GetTabTitle(ExplorerTabState tab)
    {
        if (tab.IsSearchView) return $"Search: {tab.Location.SearchQuery}";
        if (tab.Location.IsHome) return "Home";
        if (tab.Location.IsDriveList) return "This PC";
        if (tab.Location.IsRecycleBin) return "Recycle Bin";
        var path = tab.Location.Path ?? "Home";
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : path;
    }

    private void AddTab(ExplorerLocation location)
    {
        var tab = new ExplorerTabState(location);
        tab.ViewMode = ActiveTab.ViewMode;
        tab.SortColumn = ActiveTab.SortColumn;
        tab.SortAscending = ActiveTab.SortAscending;
        tab.HomeSortColumn = ActiveTab.HomeSortColumn;
        tab.HomeSortAscending = ActiveTab.HomeSortAscending;
        tab.HomeSortExplicitly = ActiveTab.HomeSortExplicitly;
        RestoreFolderViewPreferences(tab);
        _tabs.Add(tab);
        _activeTabIndex = _tabs.Count - 1;
        SyncExplorerTabs();
        ShowActiveTab();
    }

    private void CloseTab(ExplorerTabState tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;
        if (_tabs.Count == 1)
        {
            Close();
            return;
        }
        CancelSearch(tab);
        ExplorerClosedTabHistory.Remember(_closedTabs, tab);
        _tabs.RemoveAt(index);
        if (index < _activeTabIndex) _activeTabIndex--;
        else if (index == _activeTabIndex) _activeTabIndex = Math.Min(index, _tabs.Count - 1);
        SyncExplorerTabs();
        ShowActiveTab();
    }

    private void ShowActiveTab()
    {
        SearchBox.Text = _location.SearchQuery ?? string.Empty;
        RestoreColumnWidths(_location.Path);
        UpdateSortPresentation();
        ApplyExplorerViewMode(ActiveTab.ViewMode);
        if (_isSearchView && !string.IsNullOrWhiteSpace(_location.SearchQuery))
            _ = SearchCurrentFolderAsync(_location.SearchQuery);
        else
            RefreshLocation();
    }

    private void ExplorerTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTabs || ExplorerTabs.SelectedItem is not TabItem { Tag: ExplorerTabState tab }) return;
        var previous = ActiveTab;
        CancelSearch(previous);
        _activeTabIndex = _tabs.IndexOf(tab);
        ShowActiveTab();
    }

    private void ExplorerTab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TabItem tab || IsInsideTabButton(e.OriginalSource as DependencyObject, tab))
        {
            _tabDragCandidate = null;
            return;
        }

        _tabDragCandidate = tab;
        _tabDragStart = e.GetPosition(tab);
    }

    private void ExplorerTab_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not TabItem tab || !ReferenceEquals(tab, _tabDragCandidate)) return;

        var current = e.GetPosition(tab);
        if (Math.Abs(current.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _tabDragCandidate = null;
        var data = new DataObject(ExplorerTabDragFormat, new ExplorerTabDragPayload(this, (ExplorerTabState)tab.Tag));
        DragDrop.DoDragDrop(tab, data, DragDropEffects.Move);
    }

    private void ExplorerTab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(sender, _tabDragCandidate)) _tabDragCandidate = null;
    }

    private void ExplorerTab_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || sender is not TabItem { Tag: ExplorerTabState tab }) return;
        CloseTab(tab);
        e.Handled = true;
    }

    private void ExplorerTab_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TabItem { Tag: ExplorerTabState tab }) ExplorerTabs.SelectedItem = sender;
    }

    private ContextMenu CreateTabContextMenu(ExplorerTabState tab)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateTabMenuItem("Duplicate tab", tab, DuplicateTab_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateTabMenuItem("Close tab", tab, CloseTab_Click));
        menu.Items.Add(CreateTabMenuItem("Close other tabs", tab, CloseOtherTabs_Click));
        menu.Items.Add(CreateTabMenuItem("Close tabs to the right", tab, CloseTabsToRight_Click));
        menu.Opened += (_, _) =>
        {
            var anchorIndex = _tabs.IndexOf(tab);
            if (menu.Items[3] is MenuItem closeOthers) closeOthers.IsEnabled = _tabs.Count > 1;
            if (menu.Items[4] is MenuItem closeRight) closeRight.IsEnabled = anchorIndex >= 0 && anchorIndex < _tabs.Count - 1;
        };
        return menu;
    }

    private static MenuItem CreateTabMenuItem(string header, ExplorerTabState tab, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header, Tag = tab };
        item.Click += handler;
        return item;
    }

    private void DuplicateTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ExplorerTabState tab } || !_tabs.Contains(tab)) return;
        var duplicate = tab.Duplicate();
        _tabs.Insert(_tabs.IndexOf(tab) + 1, duplicate);
        _activeTabIndex = _tabs.IndexOf(duplicate);
        SyncExplorerTabs();
        ShowActiveTab();
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ExplorerTabState tab }) CloseTab(tab);
    }

    private void CloseOtherTabs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ExplorerTabState tab }) CloseTabs(tab, closeOtherTabs: true);
    }

    private void CloseTabsToRight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ExplorerTabState tab }) CloseTabs(tab, closeOtherTabs: false);
    }

    private void CloseTabs(ExplorerTabState anchor, bool closeOtherTabs)
    {
        var closing = ExplorerTabManagement.GetTabsToClose(_tabs, anchor, closeOtherTabs);
        if (closing.Count == 0) return;
        var activeTab = ActiveTab;
        var oldActiveIndex = _activeTabIndex;
        foreach (var tab in closing)
        {
            CancelSearch(tab);
            ExplorerClosedTabHistory.Remember(_closedTabs, tab);
            _tabs.Remove(tab);
        }
        _activeTabIndex = _tabs.Contains(activeTab) ? _tabs.IndexOf(activeTab) : Math.Min(oldActiveIndex, _tabs.Count - 1);
        SyncExplorerTabs();
        ShowActiveTab();
    }

    private void ExplorerTab_DragOver(object sender, DragEventArgs e)
    {
        var canAccept = sender is TabItem { Tag: ExplorerTabState }
            && e.Data.GetData(ExplorerTabDragFormat) is ExplorerTabDragPayload payload
            && payload.Source._tabs.Contains(payload.Tab);
        e.Effects = canAccept ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ExplorerTab_Drop(object sender, DragEventArgs e)
    {
        if (sender is not TabItem { Tag: ExplorerTabState targetTab } target
            || e.Data.GetData(ExplorerTabDragFormat) is not ExplorerTabDragPayload payload
            || !payload.Source._tabs.Contains(payload.Tab)) return;

        var source = payload.Source;
        var draggedTab = payload.Tab;
        var targetIndex = _tabs.IndexOf(targetTab);
        if (targetIndex < 0) return;
        var insertionIndex = targetIndex + (e.GetPosition(target).X >= target.ActualWidth / 2 ? 1 : 0);

        if (!ReferenceEquals(source, this))
        {
            var transferSourceIndex = source._tabs.IndexOf(draggedTab);
            var previouslyActive = source.ActiveTab;
            if (!ExplorerTabOrdering.Transfer(source._tabs, _tabs, transferSourceIndex, insertionIndex)) return;
            CancelSearch(draggedTab);

            if (source._tabs.Count == 0)
            {
                source._tabs.Add(new ExplorerTabState(new ExplorerLocation(null, IsHome: true)));
                source.RestoreFolderViewPreferences(source._tabs[0]);
                source._activeTabIndex = 0;
            }
            else
            {
                source._activeTabIndex = source._tabs.Contains(previouslyActive)
                    ? source._tabs.IndexOf(previouslyActive)
                    : Math.Min(transferSourceIndex, source._tabs.Count - 1);
            }

            _activeTabIndex = _tabs.IndexOf(draggedTab);
            source.SyncExplorerTabs();
            source.ShowActiveTab();
            SyncExplorerTabs();
            ShowActiveTab();
            e.Handled = true;
            return;
        }

        var sourceIndex = _tabs.IndexOf(draggedTab);
        if (sourceIndex < 0) return;

        var activeTab = ActiveTab;
        if (!ExplorerTabOrdering.Move(_tabs, sourceIndex, insertionIndex)) return;

        _activeTabIndex = _tabs.IndexOf(activeTab);
        SyncExplorerTabs();
        e.Handled = true;
    }

    private void EntriesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EntriesList, e.OriginalSource as DependencyObject) is not ListViewItem item
            || item.Content is not ExplorerEntry { IsDrive: false } entry)
        {
            _entryDragCandidate = null;
            return;
        }

        _entryDragCandidate = entry;
        _entryDragStart = e.GetPosition(EntriesList);
    }

    private void EntriesList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _entryDragCandidate = null;

    private void EntriesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_location.IsRecycleBin)
        {
            _entryDragCandidate = null;
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed || _entryDragCandidate is not { } candidate) return;
        var current = e.GetPosition(EntriesList);
        if (Math.Abs(current.X - _entryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _entryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _entryDragCandidate = null;
        var paths = EntriesList.SelectedItems.OfType<ExplorerEntry>()
            .Where(entry => !entry.IsDrive && !entry.IsRecycleBinItem)
            .Select(entry => entry.FullPath)
            .ToList();
        if (!paths.Contains(candidate.FullPath, StringComparer.OrdinalIgnoreCase)) paths = [candidate.FullPath];
        if (paths.Count == 0) return;

        var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
        DragDrop.DoDragDrop(EntriesList, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void EntriesList_DragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDropDestination(e, out var destination))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var paths = GetDroppedPaths(e.Data);
        if (paths.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var move = ExplorerDragDropPolicy.ResolveMove(paths, destination,
            e.KeyStates.HasFlag(DragDropKeyStates.ControlKey), e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey));
        e.Effects = move ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void EntriesList_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDropDestination(e, out var destination)) return;
        var paths = GetDroppedPaths(e.Data);
        if (paths.Length == 0) return;

        var move = ExplorerDragDropPolicy.ResolveMove(paths, destination,
            e.KeyStates.HasFlag(DragDropKeyStates.ControlKey), e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey));
        try
        {
            var transferred = ExplorerFileOperationService.Transfer(paths, destination, move);
            RefreshCurrentView();
            SetStatus(transferred.Count == 0
                ? "Those items are already in this folder."
                : $"{(move ? "Moved" : "Copied")} {transferred.Count:N0} item{(transferred.Count == 1 ? "" : "s")}.");
            e.Effects = transferred.Count == 0 ? DragDropEffects.None : move ? DragDropEffects.Move : DragDropEffects.Copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShowFileOperationError("Could not drop items here", ex);
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private bool TryGetDropDestination(DragEventArgs e, out string destination)
    {
        destination = string.Empty;
        if (_isSearchView || _location.IsDriveList || _location.IsHome || string.IsNullOrWhiteSpace(_location.Path)) return false;
        var destinationPath = _location.Path;
        if (ItemsControl.ContainerFromElement(EntriesList, e.OriginalSource as DependencyObject) is ListViewItem item)
        {
            if (item.Content is not ExplorerEntry { IsDirectory: true, IsDrive: false } folder) return false;
            destinationPath = folder.FullPath;
        }
        destination = destinationPath;
        return true;
    }

    private static string[] GetDroppedPaths(IDataObject data) => data.GetData(DataFormats.FileDrop, autoConvert: false) switch
    {
        string[] paths => paths,
        System.Collections.Specialized.StringCollection paths => paths.Cast<string>().ToArray(),
        _ => []
    };

    private static bool IsInsideTabButton(DependencyObject? source, TabItem tab)
    {
        while (source is not null && !ReferenceEquals(source, tab))
        {
            if (source is Button) return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => AddTab(_location with { SearchQuery = null });

    private void ExplorerTabsContextMenu_Opened(object sender, RoutedEventArgs e) =>
        ReopenClosedTabMenuItem.IsEnabled = _closedTabs.Count > 0;

    private void ReopenClosedTab_Click(object sender, RoutedEventArgs e) => ReopenClosedTab();

    private void ReopenClosedTab()
    {
        var tab = ExplorerClosedTabHistory.RestoreLast(_closedTabs);
        if (tab is null) return;
        _tabs.Add(tab);
        _activeTabIndex = _tabs.Count - 1;
        SyncExplorerTabs();
        ShowActiveTab();
    }

    private void OpenInNewTab_Click(object sender, RoutedEventArgs e)
    {
        if (ExplorerTabManagement.GetNewTabLocation(EntriesList.SelectedItem as ExplorerEntry) is { } location)
            AddTab(location);
    }

    private void OpenInNewWindow_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is ExplorerEntry { IsDirectory: true } entry)
            OpenLocationInNewWindow(new ExplorerLocation(Path.GetFullPath(entry.FullPath)));
    }

    private void OpenLocationInNewWindow(ExplorerLocation location)
    {
        var path = location.IsHome || location.IsDriveList ? null : location.Path;
        var window = new ExplorerWindow(
            initialPath: path,
            showHiddenItems: _showHiddenItems,
            hideFileExtensions: _hideFileExtensions,
            startInThisPc: location.IsDriveList,
            showRecentItems: _showRecentItems,
            quickAccessStore: _quickAccessStore,
            folderViewStore: _folderViewStore,
            sessionStore: _sessionStore,
            restoreSavedSession: false,
            saveSession: false,
            openFoldersInNewTab: _openFoldersInNewTab)
        { Owner = this };
        window.Show();
    }

    private void EntriesList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle
            || ItemsControl.ContainerFromElement(EntriesList, e.OriginalSource as DependencyObject) is not ListViewItem { Content: ExplorerEntry entry }
            || ExplorerTabManagement.GetNewTabLocation(entry) is not { } location) return;
        AddTab(location);
        e.Handled = true;
    }

    private void ExplorerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var explorerAction = ExplorerKeyboardPolicy.Resolve(e.Key, Keyboard.Modifiers, e.SystemKey);
        if (explorerAction != ExplorerKeyboardAction.None)
        {
            switch (explorerAction)
            {
                case ExplorerKeyboardAction.NavigateBack:
                    Back_Click(this, new RoutedEventArgs());
                    break;
                case ExplorerKeyboardAction.NavigateForward:
                    Forward_Click(this, new RoutedEventArgs());
                    break;
                case ExplorerKeyboardAction.CreateFolder:
                    CreateFolder();
                    break;
                case ExplorerKeyboardAction.ReopenClosedTab:
                    ReopenClosedTab();
                    break;
                case ExplorerKeyboardAction.OpenNewWindow:
                    OpenLocationInNewWindow(_location);
                    break;
                case ExplorerKeyboardAction.FocusAddress:
                    AddressBreadcrumbsScroll.Visibility = Visibility.Collapsed;
                    AddressBox.Visibility = Visibility.Visible;
                    FocusAndSelect(AddressBox);
                    break;
                case ExplorerKeyboardAction.FocusSearch:
                    FocusAndSelect(SearchBox);
                    break;
                case ExplorerKeyboardAction.NavigateParent:
                    Up_Click(this, new RoutedEventArgs());
                    break;
                case ExplorerKeyboardAction.NextPane:
                    CycleNavigationFocus(reverse: false);
                    break;
                case ExplorerKeyboardAction.PreviousPane:
                    CycleNavigationFocus(reverse: true);
                    break;
                case ExplorerKeyboardAction.ShowProperties:
                    Properties_Click(this, new RoutedEventArgs());
                    break;
            }
            e.Handled = true;
            return;
        }

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (e.Key == Key.T)
            NewTabButton_Click(this, new RoutedEventArgs());
        else if (e.Key == Key.W)
            CloseTab(ActiveTab);
        else if (e.Key == Key.Tab)
        {
            var direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            var next = (_activeTabIndex + direction + _tabs.Count) % _tabs.Count;
            ExplorerTabs.SelectedIndex = next;
        }
        else return;
        e.Handled = true;
    }

    private static void FocusAndSelect(TextBox textBox)
    {
        textBox.Focus();
        textBox.SelectAll();
    }

    private void CycleNavigationFocus(bool reverse)
    {
        Control[] controls = [AddressBox, SearchBox, EntriesList];
        var current = Array.FindIndex(controls, control => control.IsKeyboardFocusWithin);
        var direction = reverse ? -1 : 1;
        var next = current < 0
            ? (reverse ? controls.Length - 1 : 0)
            : (current + direction + controls.Length) % controls.Length;
        controls[next].Focus();
        if (controls[next] is TextBox textBox) textBox.SelectAll();
    }

    private void Navigate(ExplorerLocation target, bool addHistory = true)
    {
        if (!target.IsDriveList && !target.IsHome && !target.IsRecycleBin && (string.IsNullOrWhiteSpace(target.Path) || !Directory.Exists(target.Path)))
        {
            SetStatus("That folder is unavailable or no longer exists.");
            return;
        }

        if (addHistory)
        {
            ActiveTab.PushHistory(CurrentHistoryLocation());
        }
        CancelSearch();
        _isSearchView = false;
        _location = target.IsDriveList || target.IsHome || target.IsRecycleBin ? target : new ExplorerLocation(Path.GetFullPath(target.Path!), SearchQuery: target.SearchQuery);
        RestoreFolderViewPreferences(ActiveTab);
        UpdateSortPresentation();
        ApplyExplorerViewMode(ActiveTab.ViewMode);
        SearchBox.Text = target.SearchQuery ?? "";
        if (string.IsNullOrWhiteSpace(target.SearchQuery)) RefreshLocation();
        else
        {
            _ = SynchronizeNavigationTreeAsync();
            _ = SearchCurrentFolderAsync(target.SearchQuery);
        }
    }

    private ExplorerLocation CurrentHistoryLocation() => _location with { SearchQuery = _isSearchView ? SearchBox.Text.Trim() : null };

    private void CancelSearch()
    {
        CancelSearch(ActiveTab);
    }

    private static void CancelSearch(ExplorerTabState tab)
    {
        if (tab.SearchCancellation is null) return;
        tab.SearchCancellation.Cancel();
        tab.SearchCancellation = null;
    }

    private void RefreshLocation()
    {
        IReadOnlyList<ExplorerEntry> entries;
        string? loadError = null;
        try
        {
            entries = _location.IsRecycleBin
                ? ExplorerRecycleBinService.ReadEntries().Select(ApplyDisplayName).ToList()
                : _location.IsHome
                ? ExplorerHomeService.ReadHomeFiles(_showRecentItems, _showHiddenItems).Select(ApplyDisplayName).ToList()
                : _location.IsDriveList ? ReadDrives() : ReadDirectory(_location.Path!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or COMException)
        {
            entries = [];
            loadError = $"Could not read this location: {ex.Message}";
        }

        _entries = entries;
        ApplySort();
        EntriesList.SelectedItem = null;
        _updatingDriveGroupingControl = true;
        GroupDrivesToggle.IsChecked = ActiveTab.GroupDrives;
        GroupDrivesToggle.IsEnabled = _location.IsDriveList;
        _updatingDriveGroupingControl = false;
        UpdateSelectionCommands();
        EmptyRecycleBinButton.Visibility = ExplorerRecycleBinPolicy.ShouldShowEmptyCommand(_location.IsRecycleBin)
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyRecycleBinButton.IsEnabled = ExplorerRecycleBinPolicy.CanEmpty(_location.IsRecycleBin, entries.Count);
        NewFolderButton.IsEnabled = !_location.IsDriveList && !_location.IsHome && !_location.IsRecycleBin;
        SearchBox.IsEnabled = SearchButton.IsEnabled = !_location.IsDriveList && !_location.IsHome && !_location.IsRecycleBin;
        DeleteButton.Content = _location.IsRecycleBin ? "Delete permanently" : "Delete";
        DeleteMenuItem.Header = _location.IsRecycleBin ? "Delete permanently" : "Send to Recycle Bin";
        var title = _location.IsHome ? "Home" : _location.IsDriveList ? "This PC" : _location.IsRecycleBin ? "Recycle Bin" : Path.GetFileName(_location.Path!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(title)) title = _location.Path ?? "Home";
        LocationTitle.Text = title;
        LocationSubtitle.Text = _location.IsHome
            ? _showRecentItems ? "Recently opened files · newest first" : "Recent activity is disabled in Windows"
            : _location.IsDriveList ? "Browse available drives" : _location.IsRecycleBin ? "Restore deleted files or remove them permanently" : _location.Path;
        UpdateAddressLocation();
        UpdateExplorerTabTitles();
        BackButton.IsEnabled = _back.Count > 0;
        ForwardButton.IsEnabled = _forward.Count > 0;
        UpButton.IsEnabled = !_location.IsDriveList && !_location.IsHome && !_location.IsRecycleBin && Directory.GetParent(_location.Path!) is not null;
        EmptyMessage.Text = _location.IsRecycleBin
            ? "The Recycle Bin is empty."
            : _location.IsHome
            ? _showRecentItems ? "No recent files are available." : "Recent items are turned off in Windows."
            : "This folder is empty.";
        EmptyMessage.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetFolderDetails(entries.Count);
        if (loadError is not null) SetStatus(loadError);
        else SetStatus(FormatItemCount(entries.Count));
        _ = SynchronizeNavigationTreeAsync();
    }

    private static IReadOnlyList<ExplorerEntry> ReadDrives() => DriveInfo.GetDrives()
        .Select(ReadDrive)
        .Where(entry => entry is not null)
        .Select(entry => entry!)
        .OrderBy(entry => entry.DriveGroupOrder)
        .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private static IReadOnlyList<ExplorerNavigationNode> CreateNavigationRoots()
    {
        var userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<ExplorerNavigationNode>();
        if (!string.IsNullOrWhiteSpace(userFolder)) roots.Add(new("User folder", userFolder));
        roots.Add(new("This PC", isThisPc: true));
        return roots;
    }

    private void NavigationTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem { DataContext: ExplorerNavigationNode node }
            || node.IsPlaceholder || node.IsLoaded || node.IsLoading)
            return;

        _ = EnsureNavigationNodeChildrenLoadedAsync(node);
    }

    private Task EnsureNavigationNodeChildrenLoadedAsync(ExplorerNavigationNode node)
    {
        if (node.IsLoaded || node.IsPlaceholder) return Task.CompletedTask;
        if (node.ChildrenLoadTask is not null) return node.ChildrenLoadTask;
        node.ChildrenLoadTask = LoadNavigationNodeChildrenAsync(node);
        return node.ChildrenLoadTask;
    }

    private async Task LoadNavigationNodeChildrenAsync(ExplorerNavigationNode node)
    {
        node.IsLoading = true;
        node.Children.Clear();
        node.Children.Add(new("Loading...", isPlaceholder: true));
        try
        {
            var cancellationToken = _navigationLoadCancellation.Token;
            var result = await Task.Run(() => node.IsThisPc
                ? ExplorerNavigationService.ReadDriveRoots()
                : ExplorerNavigationService.ReadDirectories(node.Path!, _showHiddenItems, cancellationToken), cancellationToken);
            node.Children.Clear();
            if (result.Error is not null)
            {
                node.Children.Add(new("Loading...", isPlaceholder: true));
                node.IsExpanded = false;
                SetStatus($"Could not load navigation folders: {result.Error}");
                return;
            }

            foreach (var directory in result.Directories)
                node.Children.Add(new(directory.Name, directory.Path));
            node.IsLoaded = true;
        }
        catch (OperationCanceledException) when (_navigationLoadCancellation.IsCancellationRequested)
        {
            node.Children.Clear();
        }
        catch (Exception ex)
        {
            Trace.TraceError("Could not load Explorer navigation tree node {0}: {1}", node.Path ?? node.Label, ex);
            node.Children.Clear();
            node.Children.Add(new("Loading...", isPlaceholder: true));
            node.IsExpanded = false;
            SetStatus($"Could not load navigation folders: {ex.Message}");
        }
        finally
        {
            node.IsLoading = false;
            node.ChildrenLoadTask = null;
        }
    }

    private async Task SynchronizeNavigationTreeAsync()
    {
        var generation = ++_navigationSyncGeneration;
        if (_location.IsHome || _location.IsRecycleBin)
        {
            SetNavigationSelection(null);
            return;
        }

        var thisPc = _navigationRoots.FirstOrDefault(node => node.IsThisPc);
        if (_location.IsDriveList)
        {
            if (thisPc is null) return;
            await EnsureNavigationNodeChildrenLoadedAsync(thisPc);
            if (generation != _navigationSyncGeneration) return;
            thisPc.IsExpanded = true;
            SetNavigationSelection(thisPc);
            return;
        }

        if (_location.Path is not { } activePath) return;
        var targetPath = Path.GetFullPath(activePath);
        var rootNode = _navigationRoots.FirstOrDefault(node => node.Path is { } path
            && ExplorerNavigationPathPolicy.IsSameOrDescendant(path, targetPath));
        IReadOnlyList<string> remainingSegments;
        if (rootNode is not null)
        {
            remainingSegments = ExplorerNavigationPathPolicy.GetRelativeSegments(rootNode.Path!, targetPath);
        }
        else
        {
            if (thisPc is null)
            {
                SetNavigationSelection(null);
                return;
            }
            await EnsureNavigationNodeChildrenLoadedAsync(thisPc);
            if (generation != _navigationSyncGeneration) return;
            rootNode = thisPc.Children.FirstOrDefault(node => node.Path is { } path
                && ExplorerNavigationPathPolicy.IsSameOrDescendant(path, targetPath));
            if (rootNode is null)
            {
                SetNavigationSelection(null);
                return;
            }
            thisPc.IsExpanded = true;
            remainingSegments = ExplorerNavigationPathPolicy.GetRelativeSegments(rootNode.Path!, targetPath);
        }

        var currentNode = rootNode;
        foreach (var segment in remainingSegments)
        {
            await EnsureNavigationNodeChildrenLoadedAsync(currentNode);
            if (generation != _navigationSyncGeneration) return;
            currentNode.IsExpanded = true;
            var childPath = Path.Combine(currentNode.Path!, segment);
            currentNode = currentNode.Children.FirstOrDefault(child => child.Path is { } path
                && string.Equals(Path.GetFullPath(path), Path.GetFullPath(childPath), StringComparison.OrdinalIgnoreCase))!;
            if (currentNode is null)
            {
                SetNavigationSelection(null);
                return;
            }
        }

        SetNavigationSelection(currentNode);
    }

    private void SetNavigationSelection(ExplorerNavigationNode? selectedNode)
    {
        _syncingNavigationSelection = true;
        try
        {
            foreach (var node in EnumerateNavigationNodes(_navigationRoots)) node.IsSelected = false;
            if (selectedNode is not null) selectedNode.IsSelected = true;
        }
        finally
        {
            _syncingNavigationSelection = false;
        }
    }

    private static IEnumerable<ExplorerNavigationNode> EnumerateNavigationNodes(IEnumerable<ExplorerNavigationNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var descendant in EnumerateNavigationNodes(node.Children.Where(child => !child.IsPlaceholder)))
                yield return descendant;
        }
    }

    private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingNavigationSelection || e.NewValue is not ExplorerNavigationNode node || node.IsPlaceholder) return;
        if (node.IsThisPc) Navigate(new ExplorerLocation(null, IsDriveList: true));
        else if (node.Path is { } path) Navigate(new ExplorerLocation(path));
    }

    private static ExplorerEntry? ReadDrive(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady) return null;
            var (order, group) = ExplorerDriveCatalog.GetGroup(drive.DriveType);
            var root = drive.RootDirectory.FullName;
            var label = drive.VolumeLabel;
            long? capacityBytes = null;
            long? freeBytes = null;
            try
            {
                var capacity = drive.TotalSize;
                if (capacity > 0)
                {
                    capacityBytes = capacity;
                    freeBytes = Math.Clamp(drive.AvailableFreeSpace, 0, capacity);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
            {
                Trace.TraceWarning("Could not read capacity for drive {0}: {1}", drive.Name, ex.Message);
            }

            var displayName = string.IsNullOrWhiteSpace(label)
                ? drive.Name
                : $"{label} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)})";
            return new ExplorerEntry(displayName, root, true, true, capacityBytes, drive.RootDirectory.LastWriteTime)
            {
                DriveType = drive.DriveType,
                DriveGroupOrder = order,
                DriveGroup = group,
                DriveCapacityBytes = capacityBytes,
                DriveFreeBytes = freeBytes
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceWarning("Could not inspect drive {0}: {1}", drive.Name, ex.Message);
            return null;
        }
    }

    private IReadOnlyList<ExplorerEntry> ReadDirectory(string path)
    {
        return Directory.EnumerateFileSystemEntries(path)
            .Select(ReadEntry)
            .Where(entry => !entry.IsSystem && (_showHiddenItems || !entry.IsHidden))
            .Select(ApplyDisplayName)
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static ExplorerEntry ReadEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        FileSystemInfo fileSystemInfo = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        var modified = fileSystemInfo.LastWriteTime;
        var entry = isDirectory
            ? new ExplorerEntry(Path.GetFileName(path), path, true, false, null, modified)
            : new ExplorerEntry(Path.GetFileName(path), path, false, false, ((FileInfo)fileSystemInfo).Length, modified);
        return entry with
        {
            IsReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0,
            IsHidden = (attributes & FileAttributes.Hidden) != 0,
            IsSystem = (attributes & FileAttributes.System) != 0,
            Created = fileSystemInfo.CreationTime,
            Accessed = fileSystemInfo.LastAccessTime
        };
    }

    private ExplorerEntry ApplyDisplayName(ExplorerEntry entry) => entry with
    {
        DisplayName = entry.GetDisplayName(_hideFileExtensions),
        IsCut = _cutPaths.Contains(entry.FullPath)
    };

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        var target = ActiveTab.GoBack(CurrentHistoryLocation());
        if (target is null) return;
        Navigate(target, addHistory: false);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        var target = ActiveTab.GoForward(CurrentHistoryLocation());
        if (target is null) return;
        Navigate(target, addHistory: false);
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (!_location.IsDriveList && !_location.IsHome && !_location.IsRecycleBin && Directory.GetParent(_location.Path!) is { } parent) Navigate(new ExplorerLocation(parent.FullName));
    }

    private void Go_Click(object sender, RoutedEventArgs e) => NavigateFromAddress();
    private void AddressBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) NavigateFromAddress(); }

    private void UpdateAddressLocation()
    {
        AddressBox.Text = _location.IsHome ? "Home" : _location.IsDriveList ? "This PC" : _location.IsRecycleBin ? "Recycle Bin" : _location.Path;
        AddressBreadcrumbs.Children.Clear();
        if (_location.IsHome || _location.IsDriveList || _location.IsRecycleBin)
        {
            var label = _location.IsHome ? "Home" : _location.IsDriveList ? "This PC" : "Recycle Bin";
            var shortcut = new Button
            {
                Content = label,
                Tag = label,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 2, 0),
                BorderThickness = new Thickness(0),
                ToolTip = label
            };
            shortcut.Click += Breadcrumb_Click;
            AddressBreadcrumbs.Children.Add(shortcut);
        }
        else if (_location.Path is { } path)
        {
            var segments = ExplorerBreadcrumbPolicy.Create(path);
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                if (index > 0)
                {
                    var separator = new TextBlock
                    {
                        Text = "›",
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)FindResource("DesktopMutedTextBrush"),
                        Margin = new Thickness(1, 0, 3, 0)
                    };
                    AddressBreadcrumbs.Children.Add(separator);
                }

                AddressBreadcrumbs.Children.Add(CreateBreadcrumbSegment(segment, index == segments.Count - 1));
            }
        }

        AddressBox.Visibility = Visibility.Collapsed;
        AddressBreadcrumbsScroll.Visibility = Visibility.Visible;
    }

    private FrameworkElement CreateBreadcrumbSegment(ExplorerBreadcrumbSegment segment, bool isCurrent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (isCurrent)
        {
            row.Children.Add(new TextBlock
            {
                Text = segment.Label,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("DesktopPrimaryTextBrush"),
                Margin = new Thickness(4, 0, 2, 0),
                ToolTip = segment.Path
            });
        }
        else
        {
            var ancestor = new Button
            {
                Content = segment.Label,
                Tag = segment.Path,
                Padding = new Thickness(7, 4, 2, 4),
                Margin = new Thickness(0),
                BorderThickness = new Thickness(0),
                ToolTip = segment.Path
            };
            ancestor.Click += Breadcrumb_Click;
            row.Children.Add(ancestor);
        }

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Loading folders…", IsEnabled = false });
        CancellationTokenSource? loadCancellation = null;
        menu.Opened += async (_, _) =>
        {
            loadCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            loadCancellation = cancellation;
            await LoadBreadcrumbFoldersAsync(menu, segment.Path, cancellation);
            if (ReferenceEquals(loadCancellation, cancellation)) loadCancellation = null;
            cancellation.Dispose();
        };
        menu.Closed += (_, _) => loadCancellation?.Cancel();

        var dropdown = new Button
        {
            Content = "⌄",
            Padding = new Thickness(4, 2, 5, 3),
            Margin = new Thickness(0, 0, 3, 0),
            BorderThickness = new Thickness(0),
            ToolTip = $"Browse folders in {segment.Path}",
            ContextMenu = menu
        };
        System.Windows.Automation.AutomationProperties.SetName(dropdown, $"Browse folders in {segment.Label}");
        dropdown.Click += (_, _) =>
        {
            menu.PlacementTarget = dropdown;
            menu.IsOpen = true;
        };
        row.Children.Add(dropdown);
        return row;
    }

    private async Task LoadBreadcrumbFoldersAsync(ContextMenu menu, string folderPath, CancellationTokenSource cancellation)
    {
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = "Loading folders…", IsEnabled = false });
        try
        {
            var result = await Task.Run(
                () => ExplorerNavigationService.ReadDirectories(folderPath, _showHiddenItems, cancellation.Token),
                cancellation.Token);
            if (cancellation.IsCancellationRequested || !menu.IsOpen) return;

            menu.Items.Clear();
            if (result.Error is not null)
            {
                menu.Items.Add(new MenuItem { Header = "Could not read folders", IsEnabled = false, ToolTip = result.Error });
                return;
            }
            if (result.Directories.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "No subfolders", IsEnabled = false });
                return;
            }

            foreach (var directory in result.Directories)
            {
                var item = new MenuItem { Header = directory.Name, Tag = directory.Path };
                item.Click += BreadcrumbFolder_Click;
                menu.Items.Add(item);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceError("Could not load Explorer breadcrumb folders at {0}: {1}", folderPath, ex);
            if (!cancellation.IsCancellationRequested && menu.IsOpen)
            {
                menu.Items.Clear();
                menu.Items.Add(new MenuItem { Header = "Could not read folders", IsEnabled = false, ToolTip = ex.Message });
            }
        }
    }

    private void BreadcrumbFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string path }) Navigate(new ExplorerLocation(path));
    }

    private void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target }) return;
        if (string.Equals(target, "Home", StringComparison.OrdinalIgnoreCase))
            Navigate(new ExplorerLocation(null, IsHome: true));
        else if (string.Equals(target, "This PC", StringComparison.OrdinalIgnoreCase))
            Navigate(new ExplorerLocation(null, IsDriveList: true));
        else if (string.Equals(target, "Recycle Bin", StringComparison.OrdinalIgnoreCase))
            Navigate(new ExplorerLocation(null, IsRecycleBin: true));
        else
            Navigate(new ExplorerLocation(target));
    }

    private void NavigateFromAddress()
    {
        var entered = AddressBox.Text.Trim();
        if (string.Equals(entered, "Home", StringComparison.OrdinalIgnoreCase))
        {
            Navigate(new ExplorerLocation(null, IsHome: true));
            return;
        }
        if (string.Equals(entered, "This PC", StringComparison.OrdinalIgnoreCase))
        {
            Navigate(new ExplorerLocation(null, IsDriveList: true));
            return;
        }
        if (IsRecycleBinAddress(entered))
        {
            Navigate(new ExplorerLocation(null, IsRecycleBin: true));
            return;
        }
        try { Navigate(new ExplorerLocation(Environment.ExpandEnvironmentVariables(entered))); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { SetStatus($"That path is not valid: {ex.Message}"); }
    }

    private static bool IsRecycleBinAddress(string? value) =>
        string.Equals(value?.Trim(), "Recycle Bin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase);

    private void QuickLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target }) return;
        if (target == "home") { Navigate(new ExplorerLocation(null, IsHome: true)); return; }
        if (target == "drives") { Navigate(new ExplorerLocation(null, IsDriveList: true)); return; }
        if (target == "recycle-bin") { Navigate(new ExplorerLocation(null, IsRecycleBin: true)); return; }
        var specialFolder = target switch
        {
            "desktop" => Environment.SpecialFolder.DesktopDirectory,
            "documents" => Environment.SpecialFolder.MyDocuments,
            "downloads" => Environment.SpecialFolder.UserProfile,
            "pictures" => Environment.SpecialFolder.MyPictures,
            _ => Environment.SpecialFolder.UserProfile
        };
        var path = Environment.GetFolderPath(specialFolder);
        if (target == "downloads") path = Path.Combine(path, "Downloads");
        Navigate(new ExplorerLocation(path));
    }

    private void RefreshQuickAccessPins()
    {
        QuickAccessPinsPanel.Children.Clear();
        foreach (var pin in _quickAccessStore.Load())
        {
            var button = new Button
            {
                Content = new TextBlock { Text = pin.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 138 },
                Tag = pin.Path,
                ToolTip = pin.Path,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                AllowDrop = true,
                Padding = new Thickness(10, 8, 6, 8)
            };
            button.Click += QuickAccessPin_Click;
            button.PreviewMouseLeftButtonDown += QuickAccessPin_PreviewMouseLeftButtonDown;
            button.PreviewMouseLeftButtonUp += QuickAccessPin_PreviewMouseLeftButtonUp;
            button.PreviewMouseMove += QuickAccessPin_PreviewMouseMove;
            button.DragOver += QuickAccessPin_DragOver;
            button.Drop += QuickAccessPin_Drop;
            var menu = new ContextMenu();
            var removeItem = new MenuItem { Header = "Remove from quick access", Tag = pin.Path };
            removeItem.Click += RemoveQuickAccessPin_Click;
            menu.Items.Add(removeItem);
            button.ContextMenu = menu;
            QuickAccessPinsPanel.Children.Add(button);
        }
    }

    private void QuickAccessPin_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressQuickAccessClick)
        {
            _suppressQuickAccessClick = false;
            return;
        }
        if (sender is not Button { Tag: string path }) return;
        Navigate(new ExplorerLocation(path));
    }

    private void QuickAccessPin_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: string path } button) return;
        _suppressQuickAccessClick = false;
        _quickAccessDragCandidate = path;
        _quickAccessDragStart = e.GetPosition(button);
    }

    private void QuickAccessPin_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: string path } && string.Equals(path, _quickAccessDragCandidate, StringComparison.OrdinalIgnoreCase))
            _quickAccessDragCandidate = null;
    }

    private void QuickAccessPin_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button { Tag: string path } button
            || !string.Equals(path, _quickAccessDragCandidate, StringComparison.OrdinalIgnoreCase)) return;
        var current = e.GetPosition(button);
        if (Math.Abs(current.X - _quickAccessDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _quickAccessDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _quickAccessDragCandidate = null;
        var data = new DataObject(QuickAccessPinDragFormat, path);
        DragDrop.DoDragDrop(button, data, DragDropEffects.Move);
        _suppressQuickAccessClick = true;
    }

    private void QuickAccessPin_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(QuickAccessPinDragFormat)
            ? DragDropEffects.Move
            : GetDroppedQuickAccessFolders(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void QuickAccessPin_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button { Tag: string targetPath } target) return;
        var targetIndex = QuickAccessPinsPanel.Children.OfType<Button>().ToList().FindIndex(button =>
            string.Equals(button.Tag as string, targetPath, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0) return;
        var insertionIndex = targetIndex + (e.GetPosition(target).Y >= target.ActualHeight / 2 ? 1 : 0);
        if (e.Data.GetDataPresent(QuickAccessPinDragFormat) && e.Data.GetData(QuickAccessPinDragFormat) is string sourcePath)
            ReorderQuickAccessPin(sourcePath, insertionIndex);
        else
            PinDroppedFolders(GetDroppedQuickAccessFolders(e.Data), insertionIndex);
        e.Handled = true;
    }

    private void QuickAccessPinsPanel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(QuickAccessPinDragFormat)
            ? DragDropEffects.Move
            : GetDroppedQuickAccessFolders(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void QuickAccessPinsPanel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(QuickAccessPinDragFormat)
            && e.Data.GetData(QuickAccessPinDragFormat) is string sourcePath)
            ReorderQuickAccessPin(sourcePath, QuickAccessPinsPanel.Children.Count);
        else
            PinDroppedFolders(GetDroppedQuickAccessFolders(e.Data), QuickAccessPinsPanel.Children.Count);
        e.Handled = true;
    }

    private static IReadOnlyList<string> GetDroppedQuickAccessFolders(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop, autoConvert: false)) return [];
        IEnumerable<string> paths = data.GetData(DataFormats.FileDrop, autoConvert: false) switch
        {
            string[] values => values,
            StringCollection values => values.Cast<string>(),
            _ => Array.Empty<string>()
        };
        return ExplorerQuickAccessCatalog.GetDroppableFolders(paths, Directory.Exists);
    }

    private void PinDroppedFolders(IReadOnlyList<string> paths, int insertionIndex)
    {
        if (paths.Count == 0) return;
        try
        {
            var added = new List<string>();
            foreach (var path in paths)
                if (_quickAccessStore.Add(path)) added.Add(path);
            if (added.Count == 0)
            {
                SetStatus("These folders are already pinned or quick access has reached its limit.");
                return;
            }

            for (var index = 0; index < added.Count; index++)
                _quickAccessStore.Move(added[index], insertionIndex + index);
            RefreshQuickAccessPins();
            SetStatus(added.Count == 1 ? "Pinned folder to quick access." : $"Pinned {added.Count} folders to quick access.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            RefreshQuickAccessPins();
            SetStatus($"Could not pin dropped folders: {ex.Message}");
        }
    }

    private void ReorderQuickAccessPin(string path, int insertionIndex)
    {
        try
        {
            if (!_quickAccessStore.Move(path, insertionIndex)) return;
            RefreshQuickAccessPins();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not reorder quick access folders: {ex.Message}");
        }
    }

    private void PinQuickAccess_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not ExplorerEntry { IsDirectory: true, IsDrive: false } entry) return;
        try
        {
            if (!_quickAccessStore.Add(entry.FullPath))
            {
                SetStatus("This folder is already pinned or quick access has reached its limit.");
                return;
            }
            RefreshQuickAccessPins();
            SetStatus($"Pinned {entry.DisplayName} to quick access.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus($"Could not pin this folder: {ex.Message}");
        }
    }

    private void UnpinQuickAccess_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not ExplorerEntry { IsDirectory: true, IsDrive: false } entry) return;
        RemoveQuickAccessPin(entry.FullPath);
    }

    private void RemoveQuickAccessPin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string path }) RemoveQuickAccessPin(path);
    }

    private void RemoveQuickAccessPin(string path)
    {
        try
        {
            if (!_quickAccessStore.Remove(path)) return;
            RefreshQuickAccessPins();
            SetStatus("Removed folder from quick access.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not remove this quick access folder: {ex.Message}");
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => _ = SearchCurrentFolderAsync(SearchBox.Text);
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _ = SearchCurrentFolderAsync(SearchBox.Text);
        e.Handled = true;
    }

    private async Task SearchCurrentFolderAsync(string query)
    {
        var tab = ActiveTab;
        var searchTerm = query.Trim();
        CancelSearch(tab);
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            tab.IsSearchView = false;
            tab.Location = tab.Location with { SearchQuery = null };
            if (ReferenceEquals(ActiveTab, tab)) RefreshLocation();
            return;
        }
        if (tab.Location.IsDriveList || tab.Location.IsHome || string.IsNullOrWhiteSpace(tab.Location.Path))
        {
            SetStatus("Open a folder before searching its contents.");
            return;
        }

        var searchRoot = tab.Location.Path!;
        var cancellation = new CancellationTokenSource();
        tab.SearchCancellation = cancellation;
        tab.IsSearchView = true;
        tab.Location = tab.Location with { SearchQuery = searchTerm };
        if (ReferenceEquals(ActiveTab, tab))
        {
            EntriesList.ItemsSource = null;
            EntriesList.SelectedItem = null;
            UpdateSelectionCommands();
            NewFolderButton.IsEnabled = false;
            LocationTitle.Text = $"Search results for “{searchTerm}”";
            LocationSubtitle.Text = $"Searching this folder and its subfolders in {searchRoot}";
            UpdateExplorerTabTitles();
            EmptyMessage.Visibility = Visibility.Collapsed;
            SetFolderDetails(0);
            SetStatus("Searching…");
        }

        try
        {
            var result = await ExplorerSearchService.SearchAsync(searchRoot, searchTerm, cancellation.Token, _showHiddenItems);
            if (cancellation.IsCancellationRequested) return;
            tab.Entries = result.Entries.Select(ApplyDisplayName).ToList();
            if (ReferenceEquals(ActiveTab, tab))
            {
                ApplySort();
                LocationSubtitle.Text = $"Search in {searchRoot}";
                EmptyMessage.Text = $"No items match “{searchTerm}”.";
                EmptyMessage.Visibility = tab.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                SetFolderDetails(tab.Entries.Count);
                var statusParts = new List<string> { $"{FormatItemCount(tab.Entries.Count)} found." };
                if (result.SkippedItems > 0) statusParts.Add($"{result.SkippedItems} inaccessible item(s) or folder(s) skipped.");
                if (result.SkippedContentItems > 0) statusParts.Add($"{result.SkippedContentItems} item(s) skipped because content search supports plain-text files up to 16 MiB.");
                SetStatus(string.Join(" ", statusParts));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                tab.Entries = [];
                if (ReferenceEquals(ActiveTab, tab))
                {
                    EntriesList.ItemsSource = tab.Entries;
                    EmptyMessage.Text = "Search could not complete.";
                    EmptyMessage.Visibility = Visibility.Visible;
                    LocationSubtitle.Text = $"Search in {searchRoot}";
                    SetFolderDetails(0);
                    SetStatus($"Search failed: {ex.Message}");
                }
            }
        }
        finally
        {
            if (ReferenceEquals(tab.SearchCancellation, cancellation)) tab.SearchCancellation = null;
            cancellation.Dispose();
        }
    }

    private void RefreshCurrentView()
    {
        if (_isSearchView) _ = SearchCurrentFolderAsync(SearchBox.Text);
        else RefreshLocation();
    }

    private void SortColumn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader { Tag: string columnName }
            || !Enum.TryParse(columnName, out ExplorerSortColumn column)) return;

        if (_location.IsHome) ActiveTab.HomeSortExplicitly = true;
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        UpdateSortPresentation();
        ApplySort();
        SaveActiveFolderViewPreferences();
    }

    private void ViewModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingViewModeControl || ViewModeSelector.SelectedItem is not ComboBoxItem { Tag: string modeName }
            || !Enum.TryParse(modeName, out ExplorerViewMode mode) || !Enum.IsDefined(mode)) return;

        ActiveTab.ViewMode = mode;
        ApplyExplorerViewMode(mode);
        SaveActiveFolderViewPreferences();
    }

    private void ApplyExplorerViewMode(ExplorerViewMode mode)
    {
        var option = ExplorerViewModeCatalog.Get(mode);
        _updatingViewModeControl = true;
        try
        {
            ViewModeSelector.SelectedItem = ViewModeSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, mode.ToString(), StringComparison.Ordinal));
        }
        finally { _updatingViewModeControl = false; }

        if (mode == ExplorerViewMode.Details)
        {
            EntriesList.ItemTemplate = null;
            EntriesList.ItemsPanel = (ItemsPanelTemplate)FindResource("ExplorerVerticalItemsPanel");
            EntriesList.View = ExplorerDetailsGridView;
            return;
        }

        EntriesList.View = null;
        EntriesList.ItemTemplate = (DataTemplate)FindResource(option.ItemTemplateKey!);
        EntriesList.ItemsPanel = (ItemsPanelTemplate)FindResource(option.WrapItems ? "ExplorerWrapItemsPanel" : "ExplorerVerticalItemsPanel");
    }

    private void SortBySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSortControls || SortBySelector.SelectedItem is not ComboBoxItem { Tag: string columnName }
            || !Enum.TryParse(columnName, out ExplorerSortColumn column) || column == _sortColumn) return;

        _sortColumn = column;
        _sortAscending = true;
        if (_location.IsHome) ActiveTab.HomeSortExplicitly = true;
        UpdateSortPresentation();
        ApplySort();
        SaveActiveFolderViewPreferences();
    }

    private void SortDirectionButton_Click(object sender, RoutedEventArgs e)
    {
        _sortAscending = !_sortAscending;
        if (_location.IsHome) ActiveTab.HomeSortExplicitly = true;
        UpdateSortPresentation();
        ApplySort();
        SaveActiveFolderViewPreferences();
    }

    private void RestoreFolderViewPreferences(ExplorerTabState tab)
    {
        if (tab.Location.Path is not { } path)
        {
            RestoreColumnWidths(null);
            return;
        }
        if (_folderViewStore.Load(path) is not { } preference)
        {
            RestoreColumnWidths(path);
            return;
        }
        tab.ViewMode = preference.ViewMode;
        tab.SortColumn = preference.SortColumn;
        tab.SortAscending = preference.SortAscending;
        RestoreColumnWidths(path);
    }

    private void SaveActiveFolderViewPreferences()
    {
        if (_location.Path is not { } path) return;
        _folderViewStore.Save(path, new ExplorerFolderViewPreference(
            ActiveTab.ViewMode, ActiveTab.SortColumn, ActiveTab.SortAscending, GetColumnWidths()));
    }

    private void DetailsColumnHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && _hasCompletedInitialLayout && !_restoringFolderColumnWidths) SaveActiveFolderViewPreferences();
    }

    private void RestoreColumnWidths(string? folderPath)
    {
        var widths = folderPath is null
            ? null
            : _folderViewStore.Load(folderPath)?.ColumnWidths;
        SetColumnWidths(widths ?? new ExplorerColumnWidths());
    }

    private void SetColumnWidths(ExplorerColumnWidths widths)
    {
        _restoringFolderColumnWidths = true;
        var generation = ++_columnWidthRestoreGeneration;
        try
        {
            ExplorerDetailsGridView.Columns[0].Width = widths.Name;
            ExplorerDetailsGridView.Columns[1].Width = widths.DateModified;
            ExplorerDetailsGridView.Columns[2].Width = widths.Type;
            ExplorerDetailsGridView.Columns[3].Width = widths.Size;
            ExplorerDetailsGridView.Columns[4].Width = widths.DateCreated;
            ExplorerDetailsGridView.Columns[5].Width = widths.DateAccessed;
        }
        finally
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (generation == _columnWidthRestoreGeneration) _restoringFolderColumnWidths = false;
            }));
        }
    }

    private ExplorerColumnWidths GetColumnWidths() => new(
        ExplorerDetailsGridView.Columns[0].Width,
        ExplorerDetailsGridView.Columns[1].Width,
        ExplorerDetailsGridView.Columns[2].Width,
        ExplorerDetailsGridView.Columns[3].Width,
        ExplorerDetailsGridView.Columns[4].Width,
        ExplorerDetailsGridView.Columns[5].Width);

    private void UpdateSortPresentation()
    {
        _updatingSortControls = true;
        try
        {
            SortBySelector.SelectedIndex = (int)_sortColumn;
            SortDirectionButton.Content = _sortAscending ? "Ascending ↑" : "Descending ↓";
        }
        finally { _updatingSortControls = false; }

        NameColumnHeader.Content = HeaderLabel("Name", _sortColumn == ExplorerSortColumn.Name, _sortAscending);
        var dateModifiedOption = SortBySelector.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, ExplorerSortColumn.DateModified.ToString(), StringComparison.Ordinal));
        if (dateModifiedOption is not null) dateModifiedOption.Content = _location.IsRecycleBin ? "Date deleted" : "Date modified";
        DateModifiedColumnHeader.Content = HeaderLabel(_location.IsRecycleBin ? "DateDeleted" : "DateModified", _sortColumn == ExplorerSortColumn.DateModified, _sortAscending);
        TypeColumnHeader.Content = HeaderLabel("Type", _sortColumn == ExplorerSortColumn.Type, _sortAscending);
        SizeColumnHeader.Content = HeaderLabel("Size", _sortColumn == ExplorerSortColumn.Size, _sortAscending);
        DateCreatedColumnHeader.Content = HeaderLabel("DateCreated", _sortColumn == ExplorerSortColumn.DateCreated, _sortAscending);
        DateAccessedColumnHeader.Content = HeaderLabel("DateAccessed", _sortColumn == ExplorerSortColumn.DateAccessed, _sortAscending);
    }

    private static string HeaderLabel(string key, bool sorted, bool ascending)
    {
        var label = key switch
        {
            "Name" => "Name",
            "DateModified" => "Date modified",
            "DateDeleted" => "Date deleted",
            "Type" => "Type",
            "Size" => "Size",
            "DateCreated" => "Date created",
            "DateAccessed" => "Date accessed",
            _ => key
        };
        return sorted ? $"{label} {(ascending ? "↑" : "↓")}" : label;
    }

    private void ApplySort()
    {
        if (_location.IsDriveList && ActiveTab.GroupDrives)
        {
            var groupedView = new ListCollectionView(ExplorerDriveCatalog.Sort(_entries, _sortColumn, _sortAscending).ToList());
            groupedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ExplorerEntry.DriveGroup)));
            EntriesList.ItemsSource = groupedView;
            return;
        }

        var entries = _location.IsHome && !ActiveTab.HomeSortExplicitly
            ? _entries.OrderByDescending(entry => entry.RecentAccessed ?? entry.Modified).ToList()
            : ExplorerSortPolicy.Sort(_entries, _sortColumn, _sortAscending);
        EntriesList.ItemsSource = entries;
    }

    private void GroupDrivesToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingDriveGroupingControl) return;
        ActiveTab.GroupDrives = GroupDrivesToggle.IsChecked == true;
        ApplySort();
    }

    private void OpenFoldersInNewTabToggle_Click(object sender, RoutedEventArgs e)
    {
        _openFoldersInNewTab = OpenFoldersInNewTabToggle.IsChecked == true;
        if (_tabs.Count > 0) SaveExplorerSession();
    }

    private void DetailsPaneToggle_Click(object sender, RoutedEventArgs e)
    {
        SetDetailsPaneVisibility(DetailsPaneToggle.IsChecked == true);
    }

    private void SetDetailsPaneVisibility(bool visible, bool captureCurrentHeight = true)
    {
        if (visible)
        {
            DetailsPaneToggle.IsChecked = true;
            DetailsPane.Visibility = Visibility.Visible;
            DetailsPaneSplitter.Visibility = Visibility.Visible;
            DetailsPaneSplitterRow.Height = new GridLength(8);
            DetailsPaneRow.Height = new GridLength(Math.Max(100, _detailsPaneHeight));
        }
        else
        {
            DetailsPaneToggle.IsChecked = false;
            if (captureCurrentHeight && DetailsPane.ActualHeight > 0) _detailsPaneHeight = DetailsPane.ActualHeight;
            DetailsPane.Visibility = Visibility.Collapsed;
            DetailsPaneSplitter.Visibility = Visibility.Collapsed;
            DetailsPaneSplitterRow.Height = new GridLength(0);
            DetailsPaneRow.Height = new GridLength(0);
        }
    }

    private void DetailsPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DetailsPane.Visibility == Visibility.Visible && e.NewSize.Height >= 100)
            _detailsPaneHeight = e.NewSize.Height;
    }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionCommands();
        var selection = EntriesList.SelectedItems.OfType<ExplorerEntry>().ToArray();
        if (selection.Length > 0)
        {
            var summary = ExplorerSelectionSummaryService.Resolve(selection);
            DetailsName.Text = summary.Name;
            DetailsType.Text = summary.Type;
            DetailsLocation.Text = summary.Location;
            DetailsSize.Text = summary.Size;
            DetailsModified.Text = summary.Modified;
            DetailsCreated.Text = summary.Created;
            DetailsAccessed.Text = summary.Accessed;
            SetStatus(selection.Length == 1
                ? selection[0].IsDirectory ? $"{selection[0].DisplayName} · folder" : $"{selection[0].DisplayName} · {selection[0].SizeText}"
                : summary.Name);
        }
        else
        {
            SetFolderDetails(_entries.Count);
        }
    }

    private void UpdateSelectionCommands()
    {
        var selection = EntriesList.SelectedItems.OfType<ExplorerEntry>().ToList();
        var canTransferSelection = !_location.IsRecycleBin && selection.Count > 0 && selection.All(entry => !entry.IsDrive && !entry.IsRecycleBinItem);
        CopyButton.IsEnabled = CutButton.IsEnabled = canTransferSelection;
        RenameButton.IsEnabled = !_location.IsRecycleBin && selection.Count == 1 && !selection[0].IsDrive;
        DeleteButton.IsEnabled = selection.Count > 0 && selection.All(entry => !entry.IsDrive);
        OpenSelectedButton.IsEnabled = selection.Count == 1;
        RestoreButton.IsEnabled = _location.IsRecycleBin && selection.Count > 0 && selection.All(entry => entry.IsRecycleBinItem);
        RestoreButton.Visibility = _location.IsRecycleBin ? Visibility.Visible : Visibility.Collapsed;
        OpenInNewTabButton.IsEnabled = !_location.IsRecycleBin && selection.Count == 1 && selection[0].IsDirectory;
        OpenInNewWindowButton.IsEnabled = !_location.IsRecycleBin && selection.Count == 1 && selection[0].IsDirectory;
        CopyPathButton.IsEnabled = canTransferSelection;
        NewFolderButton.IsEnabled = !_location.IsDriveList && !_location.IsHome && !_location.IsRecycleBin && !_isSearchView;
        PasteButton.IsEnabled = !ActiveTab.Location.IsDriveList && !ActiveTab.Location.IsHome && !ActiveTab.Location.IsRecycleBin && ClipboardHasFileDrop();
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (_location.IsRecycleBin) return;
        var paths = EntriesList.SelectedItems.OfType<ExplorerEntry>()
            .Where(entry => !entry.IsDrive)
            .Select(entry => entry.FullPath)
            .ToArray();
        if (paths.Length == 0) return;

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths), TextDataFormat.UnicodeText);
            SetStatus($"Copied {paths.Length:N0} path{(paths.Length == 1 ? "" : "s")}.");
        }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException)
        {
            ShowFileOperationError("Could not copy the selected path", ex);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyOrCutSelection(move: false);

    private void Cut_Click(object sender, RoutedEventArgs e) => CopyOrCutSelection(move: true);

    private void CopyOrCutSelection(bool move)
    {
        if (_location.IsRecycleBin) return;
        var paths = EntriesList.SelectedItems.OfType<ExplorerEntry>()
            .Where(entry => !entry.IsDrive)
            .Select(entry => entry.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0) return;

        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, paths, autoConvert: false);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(move ? 2 : 1)), autoConvert: false);
            Clipboard.SetDataObject(data, copy: true);
            _cutPaths.Clear();
            if (move) _cutPaths.UnionWith(paths);
            RefreshCutIndicators();
            SetStatus($"{(move ? "Cut" : "Copied")} {paths.Length:N0} item{(paths.Length == 1 ? "" : "s")}.");
        }
        catch (Exception ex)
        {
            ShowFileOperationError($"Could not {(move ? "cut" : "copy")} items", ex);
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteClipboardItems();

    private void PasteClipboardItems()
    {
        if (_location.IsDriveList || _location.IsHome || string.IsNullOrWhiteSpace(_location.Path)) return;
        try
        {
            var (paths, move) = ReadClipboardTransfer();
            if (paths.Length == 0) return;
            var transferred = ExplorerFileOperationService.Transfer(paths, _location.Path, move);
            if (move)
            {
                Clipboard.Clear();
                _cutPaths.Clear();
                RefreshCutIndicators();
            }
            RefreshCurrentView();
            SetStatus(transferred.Count == 0
                ? "Those items are already in this folder."
                : $"{(move ? "Moved" : "Copied")} {transferred.Count:N0} item{(transferred.Count == 1 ? "" : "s")}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or ExternalException)
        {
            ShowFileOperationError("Could not paste items", ex);
        }
    }

    private void RefreshCutIndicators()
    {
        var selectedPaths = EntriesList.SelectedItems.OfType<ExplorerEntry>().Select(entry => entry.FullPath).ToList();
        _entries = _entries.Select(entry => entry with { IsCut = _cutPaths.Contains(entry.FullPath) }).ToList();
        ApplySort();
        foreach (var path in selectedPaths)
            if (EntriesList.Items.OfType<ExplorerEntry>().FirstOrDefault(entry => string.Equals(entry.FullPath, path, StringComparison.OrdinalIgnoreCase)) is { } entry)
                EntriesList.SelectedItems.Add(entry);
        UpdateSelectionCommands();
    }

    private static bool ClipboardHasFileDrop()
    {
        try { return ReadClipboardTransfer().Paths.Length > 0; }
        catch (ExternalException) { return false; }
    }

    private static (string[] Paths, bool Move) ReadClipboardTransfer()
    {
        var data = Clipboard.GetDataObject();
        if (data is null) return ([], false);
        var paths = data.GetData(DataFormats.FileDrop, autoConvert: false) switch
        {
            string[] filePaths => filePaths,
            StringCollection filePaths => filePaths.Cast<string>().ToArray(),
            _ => []
        };
        var effect = data.GetData("Preferred DropEffect", autoConvert: false);
        var effectValue = effect switch
        {
            MemoryStream stream when stream.Length >= sizeof(int) => BitConverter.ToInt32(stream.ToArray(), 0),
            byte[] bytes when bytes.Length >= sizeof(int) => BitConverter.ToInt32(bytes, 0),
            int value => value,
            _ => 1
        };
        return (paths, (effectValue & 2) != 0);
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntriesList.SelectedItem is not ExplorerEntry entry) return;
        OpenEntry(entry);
    }

    private void OpenEntry(ExplorerEntry entry)
    {
        if (_location.IsRecycleBin && entry.IsRecycleBinItem)
        {
            RestoreEntries([entry]);
            return;
        }
        if (entry.IsDirectory)
        {
            var location = new ExplorerLocation(entry.FullPath);
            if (ExplorerFolderOpenPolicy.ShouldOpenInNewTab(_openFoldersInNewTab, entry.IsDirectory, entry.IsDrive)) AddTab(location);
            else Navigate(location);
            return;
        }
        try { Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            MessageBox.Show(this, ex.Message, "Could not open item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EntriesList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.C)
        {
            CopyOrCutSelection(move: false);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.X)
        {
            CopyOrCutSelection(move: true);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.V)
        {
            PasteClipboardItems();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && EntriesList.SelectedItem is ExplorerEntry selectedEntry)
        {
            OpenEntry(selectedEntry);
            e.Handled = true;
        }
        else if (e.Key == Key.Back && !_location.IsDriveList && !_location.IsHome && !_location.IsRecycleBin)
        {
            Up_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && EntriesList.SelectedItems.Count == 1 && EntriesList.SelectedItem is ExplorerEntry { IsDrive: false })
        {
            RenameSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && EntriesList.SelectedItems.Count > 0 && EntriesList.SelectedItems.OfType<ExplorerEntry>().All(entry => !entry.IsDrive))
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            RefreshCurrentView();
            e.Handled = true;
        }
    }

    private void ExplorerWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        switch (ExplorerMouseNavigationPolicy.Resolve(e.ChangedButton))
        {
            case ExplorerMouseNavigationAction.Back:
                Back_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case ExplorerMouseNavigationAction.Forward:
                Forward_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private void EntriesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EntriesList, e.OriginalSource as DependencyObject) is ListViewItem item)
            item.IsSelected = true;
    }

    private void EntriesContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var selection = EntriesList.SelectedItems.OfType<ExplorerEntry>().ToList();
        var hasTransferableSelection = !_location.IsRecycleBin && selection.Count > 0 && selection.All(entry => !entry.IsDrive && !entry.IsRecycleBinItem);
        var hasSingleSelection = selection.Count == 1;
        PropertiesMenuItem.IsEnabled = ExplorerPropertiesService.CanShowProperties(selection);
        RestoreMenuItem.Visibility = _location.IsRecycleBin ? Visibility.Visible : Visibility.Collapsed;
        RestoreMenuItem.IsEnabled = _location.IsRecycleBin && selection.Count > 0 && selection.All(entry => entry.IsRecycleBinItem);
        CopyMenuItem.IsEnabled = CutMenuItem.IsEnabled = hasTransferableSelection;
        PasteMenuItem.IsEnabled = !_location.IsDriveList && !_location.IsHome && !_location.IsRecycleBin && ClipboardHasFileDrop();
        OpenInNewTabMenuItem.IsEnabled = !_location.IsRecycleBin && hasSingleSelection && selection[0].IsDirectory;
        OpenInNewWindowMenuItem.IsEnabled = !_location.IsRecycleBin && hasSingleSelection && selection[0].IsDirectory;
        var selectedDirectory = !_location.IsRecycleBin && hasSingleSelection && selection[0].IsDirectory && !selection[0].IsDrive;
        var taskbarPinTarget = !_location.IsRecycleBin && hasSingleSelection && !selection[0].IsDrive &&
            TaskbarPinCatalog.IsSupportedTarget(selection[0].FullPath, selection[0].IsDirectory);
        var taskbarPinExists = taskbarPinTarget && _isTaskbarItemPinned?.Invoke(selection[0].FullPath) == true;
        PinTaskbarMenuItem.Visibility = taskbarPinTarget && _pinTaskbarItem is not null ? Visibility.Visible : Visibility.Collapsed;
        PinTaskbarMenuItem.IsEnabled = taskbarPinTarget && _pinTaskbarItem is not null && !taskbarPinExists;
        var isPinned = selectedDirectory && _quickAccessStore.Load().Any(pin => string.Equals(pin.Path, selection[0].FullPath, StringComparison.OrdinalIgnoreCase));
        PinQuickAccessMenuItem.Visibility = selectedDirectory && !isPinned ? Visibility.Visible : Visibility.Collapsed;
        PinQuickAccessMenuItem.IsEnabled = selectedDirectory && !isPinned;
        UnpinQuickAccessMenuItem.Visibility = isPinned ? Visibility.Visible : Visibility.Collapsed;
        UnpinQuickAccessMenuItem.IsEnabled = isPinned;
        RenameMenuItem.IsEnabled = !_location.IsRecycleBin && hasSingleSelection && !selection[0].IsDrive;
        DeleteMenuItem.IsEnabled = hasTransferableSelection;
        if (_location.IsRecycleBin) DeleteMenuItem.IsEnabled = selection.Count > 0 && selection.All(entry => entry.IsRecycleBinItem);
        NativeShellContextMenuItem.IsEnabled = CanShowNativeShellContextMenu(selection);
        if (EntriesList.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Open")) is { } openItem)
            openItem.IsEnabled = hasSingleSelection;
        NewFolderButton.IsEnabled = !_location.IsDriveList && !_location.IsHome && !_location.IsRecycleBin && !_isSearchView;
        if (EntriesList.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "New folder")) is { } newFolderItem)
            newFolderItem.IsEnabled = !_location.IsDriveList && !_location.IsHome && !_location.IsRecycleBin && !_isSearchView;
    }

    private bool CanShowNativeShellContextMenu(IReadOnlyList<ExplorerEntry> selection)
    {
        if (_location.IsRecycleBin) return false;
        if (selection.Count == 0) return !_location.IsHome && !_location.IsDriveList && _location.Path is { } folder && Directory.Exists(folder);
        if (selection.Any(entry => entry.IsDrive)) return false;
        try
        {
            _ = NativeShellContextMenuPolicy.NormalizeSelection(selection.Select(entry => entry.FullPath));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async void NativeShellContextMenu_Click(object sender, RoutedEventArgs e)
    {
        var selection = EntriesList.SelectedItems.OfType<ExplorerEntry>().ToList();
        var owner = new WindowInteropHelper(this).Handle;
        try
        {
            if (selection.Count == 0)
            {
                if (_location.Path is { } folder && !_location.IsHome && !_location.IsDriveList && !_location.IsRecycleBin)
                    await NativeShellContextMenuService.ShowForFolderBackgroundAsync(owner, folder);
            }
            else
            {
                await NativeShellContextMenuService.ShowForItemsAsync(owner, selection.Select(entry => entry.FullPath));
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open Windows' context menu: {ex.Message}");
        }
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e) => CreateFolder();
    private void PinTaskbar_Click(object sender, RoutedEventArgs e)
    {
        if (_pinTaskbarItem is null || EntriesList.SelectedItems.Count != 1 || EntriesList.SelectedItem is not ExplorerEntry entry)
            return;

        if (_pinTaskbarItem(entry.FullPath))
            SetStatus($"Pinned {entry.Name} to the taskbar.");
    }
    private void Rename_Click(object sender, RoutedEventArgs e) => RenameSelected();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCurrentView();

    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is ExplorerEntry entry) OpenEntry(entry);
    }

    private void RestoreSelected_Click(object sender, RoutedEventArgs e) =>
        RestoreEntries(EntriesList.SelectedItems.OfType<ExplorerEntry>().Where(entry => entry.IsRecycleBinItem).ToArray());

    private void EmptyRecycleBin_Click(object sender, RoutedEventArgs e)
    {
        if (!_location.IsRecycleBin) return;
        var itemCount = _entries.Count(entry => entry.IsRecycleBinItem);
        if (!ExplorerRecycleBinPolicy.CanEmpty(true, itemCount)) return;

        var result = MessageBox.Show(this,
            $"Permanently delete all {itemCount:N0} item{(itemCount == 1 ? string.Empty : "s")} in the Recycle Bin? This cannot be undone.",
            "Empty Recycle Bin", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            ExplorerRecycleBinService.Empty(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            RefreshLocation();
            SetStatus($"Emptied the Recycle Bin ({itemCount:N0} items).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            RefreshLocation();
            SetStatus($"Could not empty the Recycle Bin: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Could not empty Recycle Bin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreEntries(IReadOnlyList<ExplorerEntry> entries)
    {
        if (!_location.IsRecycleBin || entries.Count == 0) return;
        var restored = 0;
        var failures = new List<string>();
        foreach (var entry in entries)
        {
            try
            {
                ExplorerRecycleBinService.Restore(entry.ShellItemPath ?? entry.FullPath);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or COMException)
            {
                failures.Add($"{entry.Name}: {ex.Message}");
            }
        }

        RefreshLocation();
        SetStatus(failures.Count == 0
            ? $"Restored {restored:N0} item{(restored == 1 ? "" : "s")} to the original location{(restored == 1 ? "" : "s")}."
            : $"Restored {restored:N0} item{(restored == 1 ? "" : "s")}; {failures.Count:N0} failed.");
        if (failures.Count > 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, failures), "Some items could not be restored", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        var selection = EntriesList.SelectedItems.OfType<ExplorerEntry>().ToList();
        if (!ExplorerPropertiesService.CanShowProperties(selection)) return;
        var targetName = selection.Count == 1 ? selection[0].DisplayName : $"{selection.Count} selected items";
        try
        {
            if (!ExplorerPropertiesService.ShowProperties(selection, new System.Windows.Interop.WindowInteropHelper(this).Handle))
                SetStatus($"Windows could not open Properties for {targetName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            SetStatus($"Could not open Properties for {targetName}: {ex.Message}");
        }
    }

    private void CreateFolder()
    {
        if (_location.IsDriveList || _location.IsHome || _location.IsRecycleBin || _isSearchView) return;
        try
        {
            var createdPath = ExplorerFileOperationService.CreateFolder(_location.Path!);
            RefreshLocation();
            EntriesList.SelectedItem = EntriesList.Items.Cast<ExplorerEntry>().FirstOrDefault(entry => string.Equals(entry.FullPath, createdPath, StringComparison.OrdinalIgnoreCase));
            SetStatus($"Created {Path.GetFileName(createdPath)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowFileOperationError("Could not create folder", ex);
        }
    }

    private void RenameSelected()
    {
        if (_location.IsRecycleBin || EntriesList.SelectedItem is not ExplorerEntry { IsDrive: false, IsRecycleBinItem: false } entry) return;
        if (entry.IsRenaming) return;

        var currentName = Path.GetFileName(entry.FullPath);
        entry.RenameText = currentName;
        entry.IsRenaming = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!entry.IsRenaming || EntriesList.ItemContainerGenerator.ContainerFromItem(entry) is not DependencyObject container) return;
            var editor = FindVisualChild<TextBox>(container, candidate => ReferenceEquals(candidate.Tag, entry));
            if (editor is null) return;
            editor.Focus();
            editor.Select(0, ExplorerRenamePolicy.GetInitialSelectionLength(currentName, entry.IsDirectory));
        }));
    }

    private void RenameEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: ExplorerEntry entry }) return;
        if (e.Key == Key.Enter)
        {
            CommitRename(entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename(entry);
            e.Handled = true;
        }
    }

    private void RenameEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { Tag: ExplorerEntry entry }) CommitRename(entry, refreshImmediately: false);
    }

    private void CommitRename(ExplorerEntry entry, bool refreshImmediately = true)
    {
        if (!entry.IsRenaming) return;
        var sourceTab = ActiveTab;
        var sourceLocation = _location;
        var currentName = Path.GetFileName(entry.FullPath);
        var newName = entry.RenameText;
        if (string.Equals(currentName, newName, StringComparison.Ordinal))
        {
            entry.IsRenaming = false;
            return;
        }

        try
        {
            var renamedPath = ExplorerFileOperationService.Rename(entry.FullPath, newName);
            entry.IsRenaming = false;
            if (refreshImmediately)
            {
                RefreshCurrentView();
                if (!_isSearchView) EntriesList.SelectedItem = EntriesList.Items.Cast<ExplorerEntry>().FirstOrDefault(item => string.Equals(item.FullPath, renamedPath, StringComparison.OrdinalIgnoreCase));
            }
            else Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (IsLoaded && ReferenceEquals(ActiveTab, sourceTab) && _location == sourceLocation)
                    RefreshAfterRename(entry.FullPath, renamedPath);
            }));
            SetStatus($"Renamed to {Path.GetFileName(renamedPath)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            entry.IsRenaming = false;
            entry.RenameText = currentName;
            ShowFileOperationError("Could not rename item", ex);
        }
    }

    private void RefreshAfterRename(string oldPath, string renamedPath)
    {
        var selectedPaths = EntriesList.SelectedItems.OfType<ExplorerEntry>()
            .Select(entry => string.Equals(entry.FullPath, oldPath, StringComparison.OrdinalIgnoreCase) ? renamedPath : entry.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        RefreshCurrentView();
        if (_isSearchView) return;
        foreach (var entry in EntriesList.Items.OfType<ExplorerEntry>())
            if (selectedPaths.Contains(entry.FullPath)) EntriesList.SelectedItems.Add(entry);
    }

    private static void CancelRename(ExplorerEntry entry)
    {
        entry.RenameText = Path.GetFileName(entry.FullPath);
        entry.IsRenaming = false;
    }

    private static T? FindVisualChild<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && predicate(match)) return match;
            if (FindVisualChild(child, predicate) is { } descendant) return descendant;
        }
        return null;
    }

    private void DeleteSelected()
    {
        var entries = EntriesList.SelectedItems.OfType<ExplorerEntry>()
            .Where(entry => !entry.IsDrive)
            .DistinctBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(entry => entry.FullPath.Length)
            .ToList();
        if (entries.Count == 0) return;
        if (_location.IsRecycleBin)
        {
            DeletePermanently(entries);
            return;
        }
        var description = entries.Count == 1
            ? $"Send ‘{entries[0].Name}’ to the Recycle Bin?"
            : $"Send {entries.Count:N0} selected items to the Recycle Bin?";
        var result = MessageBox.Show(this, description, entries.Count == 1 ? "Delete item" : "Delete items", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        var deleted = 0;
        var failures = new List<string>();
        foreach (var entry in entries)
        {
            try
            {
                if (entry.IsDirectory) FileSystem.DeleteDirectory(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else FileSystem.DeleteFile(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                failures.Add($"{entry.Name}: {ex.Message}");
            }
        }

        RefreshCurrentView();
        SetStatus(failures.Count == 0
            ? $"Sent {deleted:N0} item{(deleted == 1 ? "" : "s")} to the Recycle Bin."
            : $"Sent {deleted:N0} item{(deleted == 1 ? "" : "s")} to the Recycle Bin; {failures.Count:N0} failed.");
        if (failures.Count > 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, failures), "Some items could not be deleted", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void DeletePermanently(IReadOnlyList<ExplorerEntry> entries)
    {
        var description = entries.Count == 1
            ? $"Permanently delete ‘{entries[0].Name}’? This cannot be undone."
            : $"Permanently delete {entries.Count:N0} selected items? This cannot be undone.";
        var result = MessageBox.Show(this, description, "Delete permanently", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        var deleted = 0;
        var failures = new List<string>();
        foreach (var entry in entries)
        {
            try
            {
                ExplorerRecycleBinService.DeletePermanently(entry.ShellItemPath ?? entry.FullPath);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or COMException)
            {
                failures.Add($"{entry.Name}: {ex.Message}");
            }
        }

        RefreshLocation();
        SetStatus(failures.Count == 0
            ? $"Permanently deleted {deleted:N0} item{(deleted == 1 ? "" : "s")}."
            : $"Permanently deleted {deleted:N0} item{(deleted == 1 ? "" : "s")}; {failures.Count:N0} failed.");
        if (failures.Count > 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, failures), "Some items could not be deleted", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ShowFileOperationError(string title, Exception ex) =>
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    private void SetFolderDetails(int itemCount)
    {
        if (_isSearchView)
        {
            DetailsName.Text = "Search results";
            DetailsType.Text = "File search";
            DetailsLocation.Text = _location.Path;
            DetailsSize.Text = FormatItemCount(itemCount);
            DetailsModified.Text = SearchBox.Text.Contains("content:", StringComparison.OrdinalIgnoreCase)
                || SearchBox.Text.Contains("contents:", StringComparison.OrdinalIgnoreCase)
                ? $"Text content matches “{SearchBox.Text.Trim()}”."
                : $"Name contains “{SearchBox.Text.Trim()}”.";
            DetailsCreated.Text = DetailsAccessed.Text = "—";
            return;
        }
        if (_location.IsHome)
        {
            DetailsName.Text = "Home";
            DetailsType.Text = "Recent files";
            DetailsLocation.Text = "Opened recently";
            DetailsSize.Text = FormatItemCount(itemCount);
            DetailsModified.Text = "Select an item to see its modified date.";
            DetailsCreated.Text = DetailsAccessed.Text = "Select an item to see its creation and access dates.";
            return;
        }
        if (_location.IsRecycleBin)
        {
            DetailsName.Text = "Recycle Bin";
            DetailsType.Text = "Deleted items";
            DetailsLocation.Text = "Original locations shown for each item";
            DetailsSize.Text = FormatItemCount(itemCount);
            DetailsModified.Text = "Select an item to see its deletion date.";
            DetailsCreated.Text = DetailsAccessed.Text = "—";
            return;
        }
        DetailsName.Text = _location.IsDriveList ? "This PC" : Path.GetFileName(_location.Path!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(DetailsName.Text)) DetailsName.Text = _location.Path ?? "Home";
        DetailsType.Text = _location.IsDriveList ? "Computer" : "Folder";
        DetailsLocation.Text = _location.IsDriveList ? "Available local drives" : _location.Path;
        DetailsSize.Text = FormatItemCount(itemCount);
        DetailsModified.Text = "Select an item to see its modified date.";
        DetailsCreated.Text = DetailsAccessed.Text = "Select an item to see its creation and access dates.";
    }

    private static string FormatItemCount(int count) => $"{count:N0} item{(count == 1 ? "" : "s")}";
    private void SetStatus(string message) => StatusText.Text = message;
}
