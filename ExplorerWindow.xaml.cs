using System.Diagnostics;
using System.IO;
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
            DetailsName.Text = entry.Name;
            DetailsType.Text = entry.Type;
            DetailsLocation.Text = entry.FullPath;
            DetailsSize.Text = entry.SizeText.Length == 0 ? (entry.IsDirectory ? "Folder" : "—") : entry.SizeText;
            DetailsModified.Text = entry.Modified == DateTime.MinValue ? "—" : entry.Modified.ToString("f");
            SetStatus(entry.IsDirectory ? $"{entry.Name} · folder" : $"{entry.Name} · {entry.SizeText}");
        }
        else SetFolderDetails(_entries.Count);
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntriesList.SelectedItem is not ExplorerEntry entry) return;
        if (entry.IsDirectory) { Navigate(new ExplorerLocation(entry.FullPath)); return; }
        try { Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            MessageBox.Show(this, ex.Message, "Could not open item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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
