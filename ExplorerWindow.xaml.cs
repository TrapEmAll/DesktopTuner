using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopTuner;

public partial class ExplorerWindow : Window
{
    private sealed record ExplorerLocation(string? Path, bool IsDriveList = false);
    private sealed record ExplorerEntry(string Name, string FullPath, bool IsDirectory, bool IsDrive, long? Length, DateTime Modified)
    {
        public string Type => IsDrive ? "Local drive" : IsDirectory ? "File folder" : System.IO.Path.GetExtension(Name) is { Length: > 1 } extension ? $"{extension[1..].ToUpperInvariant()} file" : "File";
        public string SizeText => Length is long length ? FormatSize(length) : "";
        public string ModifiedText => Modified == DateTime.MinValue ? "" : Modified.ToString("g");
    }

    private readonly List<ExplorerLocation> _back = [];
    private readonly List<ExplorerLocation> _forward = [];
    private ExplorerLocation _location;
    private IReadOnlyList<ExplorerEntry> _entries = [];

    public ExplorerWindow(string? initialPath = null)
    {
        InitializeComponent();
        var startPath = string.IsNullOrWhiteSpace(initialPath) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : initialPath;
        _location = Directory.Exists(startPath) ? new ExplorerLocation(Path.GetFullPath(startPath)) : new ExplorerLocation(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
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
            _back.Add(_location);
            _forward.Clear();
        }
        _location = target.IsDriveList ? target : new ExplorerLocation(Path.GetFullPath(target.Path!));
        RefreshLocation();
    }

    private void RefreshLocation()
    {
        var query = SearchBox.Text.Trim();
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
        var visible = string.IsNullOrEmpty(query)
            ? entries
            : entries.Where(entry => entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        EntriesList.ItemsSource = visible;
        EntriesList.SelectedItem = null;
        var title = _location.IsDriveList ? "This PC" : Path.GetFileName(_location.Path!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(title)) title = _location.Path ?? "Home";
        LocationTitle.Text = title;
        LocationSubtitle.Text = _location.IsDriveList ? "Browse available drives" : _location.Path;
        AddressBox.Text = _location.IsDriveList ? "This PC" : _location.Path;
        BackButton.IsEnabled = _back.Count > 0;
        ForwardButton.IsEnabled = _forward.Count > 0;
        UpButton.IsEnabled = !_location.IsDriveList && Directory.GetParent(_location.Path!) is not null;
        EmptyMessage.Text = string.IsNullOrEmpty(query) ? "This folder is empty." : $"No items match “{query}”.";
        EmptyMessage.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetFolderDetails(visible.Count);
        if (loadError is not null) SetStatus(loadError);
        else if (string.IsNullOrEmpty(query)) SetStatus(FormatItemCount(entries.Count));
        else SetStatus($"{visible.Count} of {FormatItemCount(entries.Count)} match “{query}”.");
    }

    private static IReadOnlyList<ExplorerEntry> ReadDrives() => DriveInfo.GetDrives()
        .Where(drive => drive.IsReady)
        .Select(drive => new ExplorerEntry(drive.Name, drive.RootDirectory.FullName, true, true, null, drive.RootDirectory.LastWriteTime))
        .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static IReadOnlyList<ExplorerEntry> ReadDirectory(string path)
    {
        return Directory.EnumerateFileSystemEntries(path)
            .Select(ReadEntry)
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static ExplorerEntry ReadEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var modified = File.GetLastWriteTime(path);
        if (isDirectory) return new ExplorerEntry(Path.GetFileName(path), path, true, false, null, modified);
        return new ExplorerEntry(Path.GetFileName(path), path, false, false, new FileInfo(path).Length, modified);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_back.Count == 0) return;
        _forward.Add(_location);
        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Navigate(target, addHistory: false);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_forward.Count == 0) return;
        _back.Add(_location);
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

    private void Search_Click(object sender, RoutedEventArgs e) => RefreshLocation();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) RefreshLocation(); }
    private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) RefreshLocation(); }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntriesList.SelectedItem is ExplorerEntry entry)
        {
            RenameButton.IsEnabled = DeleteButton.IsEnabled = !entry.IsDrive;
            DetailsName.Text = entry.Name;
            DetailsType.Text = entry.Type;
            DetailsLocation.Text = entry.FullPath;
            DetailsSize.Text = entry.SizeText.Length == 0 ? (entry.IsDirectory ? "Folder" : "—") : entry.SizeText;
            DetailsModified.Text = entry.Modified == DateTime.MinValue ? "—" : entry.Modified.ToString("f");
            SetStatus(entry.IsDirectory ? $"{entry.Name} · folder" : $"{entry.Name} · {entry.SizeText}");
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
            RefreshLocation();
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
        if (EntriesList.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "New folder")) is { } newFolderItem)
            newFolderItem.IsEnabled = !_location.IsDriveList;
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e) => CreateFolder();
    private void Rename_Click(object sender, RoutedEventArgs e) => RenameSelected();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLocation();

    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is ExplorerEntry entry) OpenEntry(entry);
    }

    private void CreateFolder()
    {
        if (_location.IsDriveList) return;
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
            RefreshLocation();
            EntriesList.SelectedItem = EntriesList.Items.Cast<ExplorerEntry>().FirstOrDefault(item => string.Equals(item.FullPath, renamedPath, StringComparison.OrdinalIgnoreCase));
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
            RefreshLocation();
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
        DetailsName.Text = _location.IsDriveList ? "This PC" : Path.GetFileName(_location.Path!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(DetailsName.Text)) DetailsName.Text = _location.Path ?? "Home";
        DetailsType.Text = _location.IsDriveList ? "Computer" : "Folder";
        DetailsLocation.Text = _location.IsDriveList ? "Available local drives" : _location.Path;
        DetailsSize.Text = FormatItemCount(itemCount);
        DetailsModified.Text = "Select an item to see its modified date.";
    }

    private static string FormatItemCount(int count) => $"{count:N0} item{(count == 1 ? "" : "s")}";
    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes:N0} B" : $"{size:N1} {units[unit]}";
    }
    private void SetStatus(string message) => StatusText.Text = message;
}
