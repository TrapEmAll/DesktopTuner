using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopTuner;

public partial class ShellNamespaceBrowserWindow : Window
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private string _location;
    private long _navigationVersion;

    public ShellNamespaceBrowserWindow(string location)
    {
        if (!DesktopShellNamespaceCatalog.IsShellNamespaceLocation(location) && !Directory.Exists(location))
            throw new ArgumentException("The location is not a Windows Shell namespace or an existing folder.", nameof(location));
        _location = location;
        InitializeComponent();
        AddressBox.Text = location;
        Title = $"{GetDisplayName(location)} — Desktop Tuner Explorer";
        Closed += (_, _) => _navigationVersion++;
        _ = NavigateAsync(location, recordHistory: false);
    }

    public void OpenLocationFromShell(string location)
    {
        _ = NavigateAsync(location);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private async Task NavigateAsync(string location, bool recordHistory = true)
    {
        try
        {
            if (!DesktopShellNamespaceCatalog.IsShellNamespaceLocation(location) && !Directory.Exists(location))
                throw new DirectoryNotFoundException("That Shell location is no longer available.");
            if (recordHistory && !string.Equals(_location, location, StringComparison.OrdinalIgnoreCase))
            {
                _back.Push(_location);
                _forward.Clear();
            }
            _location = location;
            var version = ++_navigationVersion;
            AddressBox.Text = location;
            ItemsList.ItemsSource = null;
            StatusText.Text = "Loading Shell items…";
            var entries = await DesktopShellNamespaceCatalog.ReadChildrenAsync(location);
            if (version != _navigationVersion || !IsVisible && IsLoaded) return;
            ItemsList.ItemsSource = entries;
            StatusText.Text = entries.Count == 0
                ? "This location is empty or Windows returned no items."
                : $"{entries.Count:N0} items · Loading icons…";
            var entriesWithIcons = await Task.Run(() => entries
                .Select(entry => entry with { Icon = TaskbarIconService.LoadNamespaceIcon(entry.ParsingName) })
                .ToArray());
            if (version != _navigationVersion || !IsVisible && IsLoaded) return;
            ItemsList.ItemsSource = entriesWithIcons;
            Title = $"{GetDisplayName(location)} — Desktop Tuner Explorer";
            StatusText.Text = entriesWithIcons.Length == 0 ? "This location is empty or Windows returned no items." : $"{entriesWithIcons.Length:N0} items";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_back.Count == 0) return;
        _forward.Push(_location);
        await NavigateAsync(_back.Pop(), recordHistory: false);
    }

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.Exists(_location)
            ? Directory.GetParent(_location)?.FullName
            : await DesktopShellNamespaceCatalog.ReadParentLocationAsync(_location);
        if (!string.IsNullOrWhiteSpace(parent)) await NavigateAsync(parent);
    }

    private async void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_forward.Count == 0) return;
        _back.Push(_location);
        await NavigateAsync(_forward.Pop(), recordHistory: false);
    }

    private void Go_Click(object sender, RoutedEventArgs e) => _ = NavigateFromAddressAsync();

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _ = NavigateFromAddressAsync();
        e.Handled = true;
    }

    private async Task NavigateFromAddressAsync()
    {
        var target = Environment.ExpandEnvironmentVariables(AddressBox.Text.Trim());
        if (DesktopShellNamespaceCatalog.IsShellNamespaceLocation(target) || Directory.Exists(target)) await NavigateAsync(target);
        else StatusText.Text = "Enter an existing folder or a Windows Shell namespace path.";
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedItem();

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelectedItem();

    private void OpenSelectedItem()
    {
        if (ItemsList.SelectedItem is not DesktopShellNamespaceEntry entry) return;
        if (entry.IsFolder)
        {
            _ = NavigateAsync(entry.ParsingName);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(entry.ParsingName) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Could not open Shell item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ItemContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        ShowMoreOptionsMenuItem.IsEnabled = ItemsList.SelectedItem is DesktopShellNamespaceEntry;
    }

    private async void ShowMoreOptions_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not DesktopShellNamespaceEntry entry) return;
        try
        {
            await NativeShellContextMenuService.ShowForShellItemAsync(new WindowInteropHelper(this).Handle, entry.ParsingName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open Windows' context menu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && Keyboard.Modifiers == ModifierKeys.None)
        {
            await GoUpAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            await GoBackAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt && _forward.Count > 0)
        {
            await GoForwardAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ItemsList.IsKeyboardFocusWithin)
        {
            OpenSelectedItem();
            e.Handled = true;
        }
    }

    private async Task GoUpAsync()
    {
        var parent = Directory.Exists(_location)
            ? Directory.GetParent(_location)?.FullName
            : await DesktopShellNamespaceCatalog.ReadParentLocationAsync(_location);
        if (!string.IsNullOrWhiteSpace(parent)) await NavigateAsync(parent);
    }

    private async Task GoBackAsync()
    {
        if (_back.Count == 0) return;
        _forward.Push(_location);
        await NavigateAsync(_back.Pop(), recordHistory: false);
    }

    private async Task GoForwardAsync()
    {
        if (_forward.Count == 0) return;
        _back.Push(_location);
        await NavigateAsync(_forward.Pop(), recordHistory: false);
    }

    private static string GetDisplayName(string location) => Directory.Exists(location)
        ? new DirectoryInfo(location).Name
        : location.Equals("shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase) ? "This PC"
        : location.Equals("shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase) ? "Recycle Bin"
        : location;

    private void Window_SourceInitialized(object? sender, EventArgs e) => SystemBackdropService.TryApplyMica(this);
}
