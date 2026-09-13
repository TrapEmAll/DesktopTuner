using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopTuner;

public partial class ExplorerWindow : Window
{
    private sealed record ExplorerLocation(string? Path, bool IsDriveList = false, string? SearchQuery = null);

    private readonly List<ExplorerLocation> _back = [];
    private readonly List<ExplorerLocation> _forward = [];
    private ExplorerLocation _location;
    private IReadOnlyList<ExplorerEntry> _entries = [];
    private CancellationTokenSource? _searchCancellation;
    private bool _isSearchView;
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
        _location = startInThisPc && string.IsNullOrWhiteSpace(initialPath)
            ? new ExplorerLocation(null, IsDriveList: true)
            : Directory.Exists(startPath)
                ? new ExplorerLocation(Path.GetFullPath(startPath))
                : new ExplorerLocation(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Closed += (_, _) => CancelSearch();
        RefreshLocation();
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
            _back.Add(CurrentHistoryLocation());
            _forward.Clear();
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
        if (_searchCancellation is null) return;
        _searchCancellation.Cancel();
        _searchCancellation.Dispose();
        _searchCancellation = null;
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
        NewFolderButton.IsEnabled = !_location.IsDriveList;
        var title = _location.IsDriveList ? "This PC" : Path.GetFileName(_location.Path!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(title)) title = _location.Path ?? "Home";
        LocationTitle.Text = title;
        LocationSubtitle.Text = _location.IsDriveList ? "Browse available drives" : _location.Path;
        AddressBox.Text = _location.IsDriveList ? "This PC" : _location.Path;
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
        DisplayName = entry.GetDisplayName(_hideFileExtensions)
    };

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_back.Count == 0) return;
        _forward.Add(CurrentHistoryLocation());
        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Navigate(target, addHistory: false);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_forward.Count == 0) return;
        _back.Add(CurrentHistoryLocation());
        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
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
        var searchTerm = query.Trim();
        CancelSearch();
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            _isSearchView = false;
            _location = _location with { SearchQuery = null };
            RefreshLocation();
            return;
        }
        if (_location.IsDriveList || string.IsNullOrWhiteSpace(_location.Path))
        {
            SetStatus("Open a folder before searching its contents.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        _isSearchView = true;
        _location = _location with { SearchQuery = searchTerm };
        EntriesList.ItemsSource = null;
        EntriesList.SelectedItem = null;
        NewFolderButton.IsEnabled = false;
        LocationTitle.Text = $"Search results for “{searchTerm}”";
        LocationSubtitle.Text = $"Searching this folder and its subfolders in {_location.Path}";
        EmptyMessage.Visibility = Visibility.Collapsed;
        SetFolderDetails(0);
        SetStatus("Searching…");

        try
        {
            var result = await ExplorerSearchService.SearchAsync(_location.Path, searchTerm, cancellation.Token, _showHiddenItems);
            if (!ReferenceEquals(_searchCancellation, cancellation)) return;
            _entries = result.Entries.Select(ApplyDisplayName).ToList();
            ApplySort();
            LocationSubtitle.Text = $"Search in {_location.Path}";
            EmptyMessage.Text = $"No items match “{searchTerm}”.";
            EmptyMessage.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SetFolderDetails(_entries.Count);
            SetStatus(result.SkippedItems == 0
                ? $"{FormatItemCount(_entries.Count)} found."
                : $"{FormatItemCount(_entries.Count)} found; {result.SkippedItems} inaccessible item(s) or folder(s) skipped.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _entries = [];
                EntriesList.ItemsSource = _entries;
                EmptyMessage.Text = "Search could not complete.";
                EmptyMessage.Visibility = Visibility.Visible;
                LocationSubtitle.Text = $"Search in {_location.Path}";
                SetFolderDetails(0);
                SetStatus($"Search failed: {ex.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation.Dispose();
                _searchCancellation = null;
            }
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
        if (EntriesList.SelectedItem is ExplorerEntry entry)
        {
            RenameButton.IsEnabled = DeleteButton.IsEnabled = !entry.IsDrive;
            DetailsName.Text = entry.DisplayName;
            DetailsType.Text = entry.Type;
            DetailsLocation.Text = entry.FullPath;
            DetailsSize.Text = entry.SizeText.Length == 0 ? (entry.IsDirectory ? "Folder" : "—") : entry.SizeText;
            DetailsModified.Text = entry.Modified == DateTime.MinValue ? "—" : entry.Modified.ToString("f");
            SetStatus(entry.IsDirectory ? $"{entry.DisplayName} · folder" : $"{entry.DisplayName} · {entry.SizeText}");
        }
        else
        {
            RenameButton.IsEnabled = DeleteButton.IsEnabled = false;
            SetFolderDetails(_entries.Count);
        }
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
        if (e.Key == Key.Enter && EntriesList.SelectedItem is ExplorerEntry selectedEntry)
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
        else if (e.Key == Key.F2 && EntriesList.SelectedItem is ExplorerEntry { IsDrive: false })
        {
            RenameSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && EntriesList.SelectedItem is ExplorerEntry { IsDrive: false })
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
        var hasSelection = EntriesList.SelectedItem is ExplorerEntry { IsDrive: false };
        RenameMenuItem.IsEnabled = hasSelection;
        DeleteMenuItem.IsEnabled = hasSelection;
        if (EntriesList.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "Open")) is { } openItem)
            openItem.IsEnabled = EntriesList.SelectedItem is ExplorerEntry;
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
