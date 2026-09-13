using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopTuner;

public partial class ExplorerWindow : Window
{
    private readonly List<ExplorerTabState> _tabs = [];
    private readonly HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    private int _activeTabIndex;
    private bool _syncingTabs;
    private ExplorerTabState ActiveTab => _tabs[_activeTabIndex];
    private List<ExplorerLocation> _back => ActiveTab.Back;
    private List<ExplorerLocation> _forward => ActiveTab.Forward;
    private ExplorerLocation _location { get => ActiveTab.Location; set => ActiveTab.Location = value; }
    private IReadOnlyList<ExplorerEntry> _entries { get => ActiveTab.Entries; set => ActiveTab.Entries = value; }
    private CancellationTokenSource? _searchCancellation { get => ActiveTab.SearchCancellation; set => ActiveTab.SearchCancellation = value; }
    private bool _isSearchView { get => ActiveTab.IsSearchView; set => ActiveTab.IsSearchView = value; }
    private readonly bool _showHiddenItems;
    private readonly bool _hideFileExtensions;
    private ExplorerSortColumn _sortColumn = ExplorerSortColumn.Name;
    private bool _sortAscending = true;
    private double _detailsPaneHeight = 160;

    public ExplorerWindow(string? initialPath = null, bool showHiddenItems = false, bool hideFileExtensions = true, bool startInThisPc = false)
    {
        InitializeComponent();
        _showHiddenItems = showHiddenItems;
        _hideFileExtensions = hideFileExtensions;
        var startPath = string.IsNullOrWhiteSpace(initialPath) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : initialPath;
        var initialLocation = startInThisPc && string.IsNullOrWhiteSpace(initialPath)
            ? new ExplorerLocation(null, IsDriveList: true)
            : Directory.Exists(startPath)
                ? new ExplorerLocation(Path.GetFullPath(startPath))
                : new ExplorerLocation(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _tabs.Add(new ExplorerTabState(initialLocation));
        SyncExplorerTabs();
        Closed += (_, _) => { foreach (var tab in _tabs) CancelSearch(tab); };
        RefreshLocation();
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
                ExplorerTabs.Items.Add(new TabItem { Header = header, Tag = tab, Padding = new Thickness(8, 3, 8, 3) });
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
        if (tab.Location.IsDriveList) return "This PC";
        var path = tab.Location.Path ?? "Home";
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : path;
    }

    private void AddTab(ExplorerLocation location)
    {
        var tab = new ExplorerTabState(location);
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
        _tabs.RemoveAt(index);
        if (index < _activeTabIndex) _activeTabIndex--;
        else if (index == _activeTabIndex) _activeTabIndex = Math.Min(index, _tabs.Count - 1);
        SyncExplorerTabs();
        ShowActiveTab();
    }

    private void ShowActiveTab()
    {
        SearchBox.Text = _location.SearchQuery ?? string.Empty;
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

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => AddTab(
        _location.IsDriveList ? new ExplorerLocation(null, IsDriveList: true) : new ExplorerLocation(_location.Path));

    private void OpenInNewTab_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is ExplorerEntry { IsDirectory: true } entry)
            AddTab(entry.IsDrive ? new ExplorerLocation(null, IsDriveList: true) : new ExplorerLocation(entry.FullPath));
    }

    private void ExplorerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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

    private void Navigate(ExplorerLocation target, bool addHistory = true)
    {
        if (!target.IsDriveList && (string.IsNullOrWhiteSpace(target.Path) || !Directory.Exists(target.Path)))
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
        _location = target.IsDriveList ? target : new ExplorerLocation(Path.GetFullPath(target.Path!), SearchQuery: target.SearchQuery);
        SearchBox.Text = target.SearchQuery ?? "";
        if (string.IsNullOrWhiteSpace(target.SearchQuery)) RefreshLocation();
        else _ = SearchCurrentFolderAsync(target.SearchQuery);
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
            entries = _location.IsDriveList ? ReadDrives() : ReadDirectory(_location.Path!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            entries = [];
            loadError = $"Could not read this location: {ex.Message}";
        }

        _entries = entries;
        ApplySort();
        EntriesList.SelectedItem = null;
        UpdateSelectionCommands();
        NewFolderButton.IsEnabled = !_location.IsDriveList;
        var title = _location.IsDriveList ? "This PC" : Path.GetFileName(_location.Path!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(title)) title = _location.Path ?? "Home";
        LocationTitle.Text = title;
        LocationSubtitle.Text = _location.IsDriveList ? "Browse available drives" : _location.Path;
        AddressBox.Text = _location.IsDriveList ? "This PC" : _location.Path;
        UpdateExplorerTabTitles();
        BackButton.IsEnabled = _back.Count > 0;
        ForwardButton.IsEnabled = _forward.Count > 0;
        UpButton.IsEnabled = !_location.IsDriveList && Directory.GetParent(_location.Path!) is not null;
        EmptyMessage.Text = "This folder is empty.";
        EmptyMessage.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetFolderDetails(entries.Count);
        if (loadError is not null) SetStatus(loadError);
        else SetStatus(FormatItemCount(entries.Count));
    }

    private static IReadOnlyList<ExplorerEntry> ReadDrives() => DriveInfo.GetDrives()
        .Where(drive => drive.IsReady)
        .Select(drive => new ExplorerEntry(drive.Name, drive.RootDirectory.FullName, true, true, null, drive.RootDirectory.LastWriteTime))
        .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

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
        var modified = File.GetLastWriteTime(path);
        var entry = isDirectory
            ? new ExplorerEntry(Path.GetFileName(path), path, true, false, null, modified)
            : new ExplorerEntry(Path.GetFileName(path), path, false, false, new FileInfo(path).Length, modified);
        return entry with
        {
            IsReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0,
            IsHidden = (attributes & FileAttributes.Hidden) != 0,
            IsSystem = (attributes & FileAttributes.System) != 0
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
        if (!_location.IsDriveList && Directory.GetParent(_location.Path!) is { } parent) Navigate(new ExplorerLocation(parent.FullName));
    }

    private void Go_Click(object sender, RoutedEventArgs e) => NavigateFromAddress();
    private void AddressBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) NavigateFromAddress(); }

    private void NavigateFromAddress()
    {
        var entered = AddressBox.Text.Trim();
        if (string.Equals(entered, "This PC", StringComparison.OrdinalIgnoreCase))
        {
            Navigate(new ExplorerLocation(null, IsDriveList: true));
            return;
        }
        try { Navigate(new ExplorerLocation(Environment.ExpandEnvironmentVariables(entered))); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { SetStatus($"That path is not valid: {ex.Message}"); }
    }

    private void QuickLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target }) return;
        if (target == "drives") { Navigate(new ExplorerLocation(null, IsDriveList: true)); return; }
        var specialFolder = target switch
        {
            "home" => Environment.SpecialFolder.UserProfile,
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
        if (tab.Location.IsDriveList || string.IsNullOrWhiteSpace(tab.Location.Path))
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
                SetStatus(result.SkippedItems == 0
                    ? $"{FormatItemCount(tab.Entries.Count)} found."
                    : $"{FormatItemCount(tab.Entries.Count)} found; {result.SkippedItems} inaccessible item(s) or folder(s) skipped.");
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

        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        NameColumnHeader.Content = HeaderLabel("Name", _sortColumn == ExplorerSortColumn.Name, _sortAscending);
        DateModifiedColumnHeader.Content = HeaderLabel("DateModified", _sortColumn == ExplorerSortColumn.DateModified, _sortAscending);
        TypeColumnHeader.Content = HeaderLabel("Type", _sortColumn == ExplorerSortColumn.Type, _sortAscending);
        SizeColumnHeader.Content = HeaderLabel("Size", _sortColumn == ExplorerSortColumn.Size, _sortAscending);
        ApplySort();
    }

    private static string HeaderLabel(string key, bool sorted, bool ascending)
    {
        var label = key switch
        {
            "Name" => "Name",
            "DateModified" => "Date modified",
            "Type" => "Type",
            "Size" => "Size",
            _ => key
        };
        return sorted ? $"{label} {(ascending ? "↑" : "↓")}" : label;
    }

    private void ApplySort() => EntriesList.ItemsSource = ExplorerSortPolicy.Sort(_entries, _sortColumn, _sortAscending);

    private void DetailsPaneToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DetailsPaneToggle.IsChecked == true)
        {
            DetailsPane.Visibility = Visibility.Visible;
            DetailsPaneSplitter.Visibility = Visibility.Visible;
            DetailsPaneSplitterRow.Height = new GridLength(8);
            DetailsPaneRow.Height = new GridLength(Math.Max(100, _detailsPaneHeight));
        }
        else
        {
            if (DetailsPane.ActualHeight > 0) _detailsPaneHeight = DetailsPane.ActualHeight;
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
        if (EntriesList.SelectedItems.OfType<ExplorerEntry>().FirstOrDefault() is { } entry)
        {
            DetailsName.Text = entry.DisplayName;
            DetailsType.Text = entry.Type;
            DetailsLocation.Text = entry.FullPath;
            DetailsSize.Text = entry.SizeText.Length == 0 ? (entry.IsDirectory ? "Folder" : "—") : entry.SizeText;
            DetailsModified.Text = entry.Modified == DateTime.MinValue ? "—" : entry.Modified.ToString("f");
            SetStatus(entry.IsDirectory ? $"{entry.DisplayName} · folder" : $"{entry.DisplayName} · {entry.SizeText}");
        }
        else
        {
            SetFolderDetails(_entries.Count);
        }
    }

    private void UpdateSelectionCommands()
    {
        var selection = EntriesList.SelectedItems.OfType<ExplorerEntry>().ToList();
        var canTransferSelection = selection.Count > 0 && selection.All(entry => !entry.IsDrive);
        CopyButton.IsEnabled = CutButton.IsEnabled = canTransferSelection;
        RenameButton.IsEnabled = DeleteButton.IsEnabled = selection.Count == 1 && !selection[0].IsDrive;
        PasteButton.IsEnabled = !ActiveTab.Location.IsDriveList && ClipboardHasFileDrop();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyOrCutSelection(move: false);

    private void Cut_Click(object sender, RoutedEventArgs e) => CopyOrCutSelection(move: true);

    private void CopyOrCutSelection(bool move)
    {
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
        if (_location.IsDriveList || string.IsNullOrWhiteSpace(_location.Path)) return;
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
        if (entry.IsDirectory) { Navigate(new ExplorerLocation(entry.FullPath)); return; }
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
        else if (e.Key == Key.Back && !_location.IsDriveList)
        {
            Up_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            Back_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            Forward_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && EntriesList.SelectedItems.Count == 1 && EntriesList.SelectedItem is ExplorerEntry { IsDrive: false })
        {
            RenameSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && EntriesList.SelectedItems.Count == 1 && EntriesList.SelectedItem is ExplorerEntry { IsDrive: false })
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            RefreshCurrentView();
            e.Handled = true;
        }
        else if (e.Key == Key.N && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && !_location.IsDriveList)
        {
            CreateFolder();
            e.Handled = true;
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
        var hasTransferableSelection = selection.Count > 0 && selection.All(entry => !entry.IsDrive);
        var hasSingleSelection = selection.Count == 1;
        CopyMenuItem.IsEnabled = CutMenuItem.IsEnabled = hasTransferableSelection;
        PasteMenuItem.IsEnabled = !_location.IsDriveList && ClipboardHasFileDrop();
        OpenInNewTabMenuItem.IsEnabled = hasSingleSelection && selection[0].IsDirectory;
        RenameMenuItem.IsEnabled = DeleteMenuItem.IsEnabled = hasSingleSelection && !selection[0].IsDrive;
        if (EntriesList.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Open")) is { } openItem)
            openItem.IsEnabled = hasSingleSelection;
        NewFolderButton.IsEnabled = !_location.IsDriveList && !_isSearchView;
        if (EntriesList.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "New folder")) is { } newFolderItem)
            newFolderItem.IsEnabled = !_location.IsDriveList && !_isSearchView;
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e) => CreateFolder();
    private void Rename_Click(object sender, RoutedEventArgs e) => RenameSelected();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCurrentView();

    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is ExplorerEntry entry) OpenEntry(entry);
    }

    private void CreateFolder()
    {
        if (_location.IsDriveList || _isSearchView) return;
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
        if (EntriesList.SelectedItem is not ExplorerEntry { IsDrive: false } entry) return;
        var currentName = Path.GetFileName(entry.FullPath);
        var newName = PromptForName("Rename", "New name:", currentName);
        if (newName is null || string.Equals(currentName, newName, StringComparison.Ordinal)) return;
        try
        {
            var renamedPath = ExplorerFileOperationService.Rename(entry.FullPath, newName);
            RefreshCurrentView();
            if (!_isSearchView) EntriesList.SelectedItem = EntriesList.Items.Cast<ExplorerEntry>().FirstOrDefault(item => string.Equals(item.FullPath, renamedPath, StringComparison.OrdinalIgnoreCase));
            SetStatus($"Renamed to {Path.GetFileName(renamedPath)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShowFileOperationError("Could not rename item", ex);
        }
    }

    private void DeleteSelected()
    {
        if (EntriesList.SelectedItem is not ExplorerEntry { IsDrive: false } entry) return;
        var result = MessageBox.Show(this, $"Send ‘{entry.Name}’ to the Recycle Bin?", "Delete item", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            if (entry.IsDirectory) FileSystem.DeleteDirectory(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else FileSystem.DeleteFile(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            RefreshCurrentView();
            SetStatus($"Sent {entry.Name} to the Recycle Bin.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowFileOperationError("Could not delete item", ex);
        }
    }

    private string? PromptForName(string title, string prompt, string initialValue)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Background = Background,
            FontFamily = FontFamily
        };
        var panel = new StackPanel { Margin = new Thickness(20), MinWidth = 340 };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var nameBox = new TextBox { Text = initialValue, MinWidth = 340, Padding = new Thickness(8, 6, 8, 6) };
        panel.Children.Add(nameBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 82, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var confirm = new Button { Content = "OK", IsDefault = true, MinWidth = 82, Padding = new Thickness(12, 6, 12, 6) };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
        return dialog.ShowDialog() == true ? nameBox.Text.Trim() : null;
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
            DetailsModified.Text = $"Name contains “{SearchBox.Text.Trim()}”.";
            return;
        }
        DetailsName.Text = _location.IsDriveList ? "This PC" : Path.GetFileName(_location.Path!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(DetailsName.Text)) DetailsName.Text = _location.Path ?? "Home";
        DetailsType.Text = _location.IsDriveList ? "Computer" : "Folder";
        DetailsLocation.Text = _location.IsDriveList ? "Available local drives" : _location.Path;
        DetailsSize.Text = FormatItemCount(itemCount);
        DetailsModified.Text = "Select an item to see its modified date.";
    }

    private static string FormatItemCount(int count) => $"{count:N0} item{(count == 1 ? "" : "s")}";
    private void SetStatus(string message) => StatusText.Text = message;
}
