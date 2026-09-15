using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTuner;

public partial class ShellNamespaceBrowserWindow : Window
{
    private const int MessageClipboardUpdate = 0x031D;
    private static readonly HashSet<string> _cutParsingNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private readonly DispatcherTimer _changeRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private string _location;
    private long _navigationVersion;
    private CancellationTokenSource? _searchCancellation;
    private bool _isSearchView;
    private ExplorerViewMode _viewMode = ExplorerViewMode.Details;
    private bool _updatingViewModeControl;
    private bool _isClosed;
    private bool _sourceInitialized;
    private bool _changeNotificationsAttempted;
    private DesktopShellNamespaceEntry? _dragCandidate;
    private Point _dragStart;
    private string? _registeredChangeLocation;
    private DesktopShellChangeNotificationListener? _shellChangeNotifications;
    private HwndSource? _windowSource;
    private HwndSourceHook? _windowMessageHook;
    private static uint _cutClipboardSequence;
    private bool _clipboardListenerRegistered;
    private readonly Func<string, bool>? _pinTaskbarItem;
    private readonly Func<string, bool>? _isTaskbarItemPinned;
    private readonly Func<string, bool>? _pinStartItem;
    private readonly Func<string, bool>? _isStartItemPinned;

    public ShellNamespaceBrowserWindow(string location, Func<string, bool>? pinTaskbarItem = null, Func<string, bool>? isTaskbarItemPinned = null, Func<string, bool>? pinStartItem = null, Func<string, bool>? isStartItemPinned = null)
    {
        if (!DesktopShellNamespaceCatalog.IsShellNamespaceLocation(location) && !Directory.Exists(location))
            throw new ArgumentException("The location is not a Windows Shell namespace or an existing folder.", nameof(location));
        _location = location;
        _pinTaskbarItem = pinTaskbarItem;
        _isTaskbarItemPinned = isTaskbarItemPinned;
        _pinStartItem = pinStartItem;
        _isStartItemPinned = isStartItemPinned;
        InitializeComponent();
        _changeRefreshTimer.Tick += ChangeRefreshTimer_Tick;
        ApplyViewMode(_viewMode);
        AddressBox.Text = location;
        Title = $"{GetDisplayName(location)} — Desktop Tuner Explorer";
        Closed += (_, _) =>
        {
            _isClosed = true;
            _changeRefreshTimer.Stop();
            _shellChangeNotifications?.Dispose();
            _shellChangeNotifications = null;
            var handle = new WindowInteropHelper(this).Handle;
            if (_clipboardListenerRegistered) RemoveClipboardFormatListener(handle);
            if (_windowSource is not null && _windowMessageHook is not null) _windowSource.RemoveHook(_windowMessageHook);
            _clipboardListenerRegistered = false;
            _windowSource = null;
            _windowMessageHook = null;
            CancelSearch();
            _navigationVersion++;
        };
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
            CancelSearch();
            _isSearchView = false;
            SearchBox.Text = string.Empty;
            CancelSearchButton.IsEnabled = false;
            if (recordHistory && !string.Equals(_location, location, StringComparison.OrdinalIgnoreCase))
            {
                _back.Push(_location);
                _forward.Clear();
            }
            _location = location;
            RegisterShellChangeNotifications(location);
            var version = ++_navigationVersion;
            AddressBox.Text = location;
            ItemsList.ItemsSource = null;
            StatusText.Text = "Loading Shell items…";
            var entries = await DesktopShellNamespaceCatalog.ReadChildrenAsync(location);
            if (version != _navigationVersion || !IsVisible && IsLoaded) return;
            StatusText.Text = entries.Count == 0
                ? "This location is empty or Windows returned no items."
                : $"{entries.Count:N0} items · Loading icons…";
            var entriesWithIcons = await Task.Run(() => entries
                .Select(entry => entry with { Icon = TaskbarIconService.LoadNamespaceIcon(entry.ParsingName) })
                .ToArray());
            if (version != _navigationVersion || !IsVisible && IsLoaded) return;
            ApplyCutState(entriesWithIcons);
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

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshCurrentViewAsync();

    private async Task RefreshCurrentViewAsync()
    {
        if (_isSearchView) await SearchCurrentLocationAsync();
        else await NavigateAsync(_location, recordHistory: false);
    }

    private void RegisterShellChangeNotifications(string location)
    {
        var sameLocation = string.Equals(_registeredChangeLocation, location, StringComparison.OrdinalIgnoreCase);
        if (sameLocation && (_shellChangeNotifications is not null || !_sourceInitialized || _changeNotificationsAttempted)) return;
        if (!sameLocation)
        {
            _registeredChangeLocation = location;
            _changeNotificationsAttempted = false;
            _shellChangeNotifications?.Dispose();
            _shellChangeNotifications = null;
        }
        if (!_sourceInitialized) return;
        _changeNotificationsAttempted = true;
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (source is null) return;
        try
        {
            _shellChangeNotifications = new DesktopShellChangeNotificationListener(source, location, QueueLocationRefresh);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Trace.TraceWarning($"Shell namespace change notifications are unavailable for '{location}'; use Refresh or F5 to update the view: {ex.Message}");
        }
    }

    private void QueueLocationRefresh()
    {
        if (_isClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed) return;
            _changeRefreshTimer.Stop();
            _changeRefreshTimer.Start();
        });
    }

    private async void ChangeRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _changeRefreshTimer.Stop();
        await RefreshCurrentViewAsync();
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchCurrentLocationAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        await SearchCurrentLocationAsync();
        e.Handled = true;
    }

    private async Task SearchCurrentLocationAsync()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            if (_isSearchView) await NavigateAsync(_location, recordHistory: false);
            return;
        }

        try { _ = ExplorerSearchQuery.Parse(query); }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        CancelSearch();
        var version = ++_navigationVersion;
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        _isSearchView = true;
        CancelSearchButton.IsEnabled = true;
        ItemsList.ItemsSource = null;
        Title = $"Search: {query} — {GetDisplayName(_location)} — Desktop Tuner Explorer";
        StatusText.Text = "Searching this location and its subfolders…";

        try
        {
            var result = await DesktopShellNamespaceCatalog.SearchAsync(_location, query, cancellation.Token);
            if (cancellation.IsCancellationRequested || version != _navigationVersion || !IsVisible && IsLoaded) return;
            StatusText.Text = $"{result.Entries.Count:N0} found · Loading Shell icons…";
            var entriesWithIcons = await Task.Run(() => result.Entries
                .Select(entry => entry with { Icon = TaskbarIconService.LoadNamespaceIcon(entry.ParsingName) })
                .ToArray());
            if (cancellation.IsCancellationRequested || version != _navigationVersion || !IsVisible && IsLoaded) return;
            ApplyCutState(entriesWithIcons);
            ItemsList.ItemsSource = entriesWithIcons;
            var statusParts = new List<string> { $"{entriesWithIcons.Length:N0} found." };
            if (result.SkippedItems > 0) statusParts.Add($"{result.SkippedItems:N0} item(s) skipped while searching.");
            if (result.SkippedContentItems > 0) statusParts.Add($"{result.SkippedContentItems:N0} item(s) skipped because content search is limited to plain-text files up to 16 MiB.");
            StatusText.Text = string.Join(" ", statusParts);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            if (!cancellation.IsCancellationRequested && version == _navigationVersion)
                StatusText.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
                CancelSearchButton.IsEnabled = false;
            }
            cancellation.Dispose();
        }
    }

    private void CancelSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        _isSearchView = false;
        _ = NavigateAsync(_location, recordHistory: false);
    }

    private void ViewModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingViewModeControl || ViewModeSelector.SelectedItem is not ComboBoxItem { Tag: string modeName }
            || !Enum.TryParse(modeName, out ExplorerViewMode mode) || !Enum.IsDefined(mode)) return;
        _viewMode = mode;
        ApplyViewMode(mode);
    }

    private void ApplyViewMode(ExplorerViewMode mode)
    {
        var option = ShellNamespaceViewModeCatalog.Get(mode);
        _updatingViewModeControl = true;
        try
        {
            ViewModeSelector.SelectedItem = ViewModeSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, mode.ToString(), StringComparison.Ordinal));
        }
        finally { _updatingViewModeControl = false; }

        if (mode == ExplorerViewMode.Details)
        {
            ItemsList.ItemTemplate = null;
            ItemsList.ItemsPanel = (ItemsPanelTemplate)FindResource("ShellNamespaceVerticalItemsPanel");
            ItemsList.View = ShellNamespaceDetailsGridView;
            return;
        }

        ItemsList.View = null;
        ItemsList.ItemTemplate = (DataTemplate)FindResource(option.ItemTemplateKey!);
        ItemsList.ItemsPanel = (ItemsPanelTemplate)FindResource(option.WrapItems
            ? "ShellNamespaceWrapItemsPanel"
            : "ShellNamespaceVerticalItemsPanel");
    }

    private void CancelSearch()
    {
        _searchCancellation?.Cancel();
        _searchCancellation = null;
        CancelSearchButton.IsEnabled = false;
    }

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

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(ItemsList, source) is ListViewItem { Content: DesktopShellNamespaceEntry entry })
            OpenItem(entry);
    }

    private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(ItemsList, source) is not ListViewItem { Content: DesktopShellNamespaceEntry entry }) return;
        _dragCandidate = entry;
        _dragStart = e.GetPosition(ItemsList);
    }

    private void ItemsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragCandidate = null;

    private async void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is not { } candidate) return;
        var current = e.GetPosition(ItemsList);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragCandidate = null;
        var selection = ItemsList.SelectedItems.OfType<DesktopShellNamespaceEntry>().ToArray();
        if (!selection.Contains(candidate)) selection = [candidate];
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.DragShellItemsAsync(owner, selection.Select(entry => entry.ParsingName));
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not drag Shell items", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelectedItem();

    private async void Delete_Click(object sender, RoutedEventArgs e) => await DeleteSelectedItemsAsync(shiftPressed: false);

    private async void Properties_Click(object sender, RoutedEventArgs e) => await ShowSelectedPropertiesAsync();

    private async Task ShowSelectedPropertiesAsync()
    {
        var selection = ItemsList.SelectedItems.OfType<DesktopShellNamespaceEntry>().ToArray();
        if (selection.Length == 0) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.ShowPropertiesForShellItemsAsync(owner, selection.Select(entry => entry.ParsingName));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open Properties: {ex.Message}";
        }
    }

    private async Task DeleteSelectedItemsAsync(bool shiftPressed)
    {
        var selection = ItemsList.SelectedItems.OfType<DesktopShellNamespaceEntry>().ToArray();
        if (selection.Length == 0) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.DeleteShellItemsAsync(owner, selection.Select(entry => entry.ParsingName), shiftPressed);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not delete the selected Shell items: {ex.Message}";
        }
        finally
        {
            await RefreshCurrentViewAsync();
        }
    }

    private async Task CopySelectedItemsAsync(bool cut)
    {
        var selection = ItemsList.SelectedItems.OfType<DesktopShellNamespaceEntry>().ToArray();
        if (selection.Length == 0) return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            var clipboardSequence = await NativeShellContextMenuService.CopyShellItemsToClipboardAsync(owner, selection.Select(entry => entry.ParsingName), cut);
            if (cut) SetCutState(selection, clipboardSequence);
            else ClearCutState();
            StatusText.Text = cut ? $"Cut {selection.Length:N0} item(s)." : $"Copied {selection.Length:N0} item(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not {(cut ? "cut" : "copy")} the selected Shell items: {ex.Message}";
        }
    }

    private async Task PasteIntoCurrentLocationAsync()
    {
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.PasteIntoShellFolderAsync(owner, _location);
            ClearCutState();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not paste into this Shell location: {ex.Message}";
        }
        finally
        {
            await RefreshCurrentViewAsync();
        }
    }

    private void OpenSelectedItem()
    {
        var selection = ItemsList.SelectedItems.OfType<DesktopShellNamespaceEntry>().ToArray();
        if (selection.Length == 0) return;
        foreach (var entry in selection)
        {
            var action = ShellNamespaceOpenPolicy.Resolve(entry.IsFolder, selection.Length);
            if (action is ShellNamespaceOpenAction.NavigateCurrentWindow or ShellNamespaceOpenAction.UseShellHandler)
            {
                OpenItem(entry);
                continue;
            }

            try
            {
                new ShellNamespaceBrowserWindow(entry.ParsingName) { Owner = this }.Show();
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
            {
                MessageBox.Show(this, ex.Message, $"Could not open {entry.Name}", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OpenItem(DesktopShellNamespaceEntry entry)
    {
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

    private async void ItemContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var hasSelection = ItemsList.SelectedItems.Count > 0;
        OpenMenuItem.IsEnabled = ItemsList.SelectedItems.Count > 0;
        OpenWithMenuItem.Visibility = Visibility.Collapsed;
        CopyMenuItem.IsEnabled = hasSelection;
        CutMenuItem.IsEnabled = hasSelection;
        NewFolderMenuItem.IsEnabled = true;
        DeleteMenuItem.IsEnabled = hasSelection;
        PropertiesMenuItem.IsEnabled = hasSelection;
        RenameMenuItem.IsEnabled = false;
        PinStartMenuItem.Visibility = Visibility.Collapsed;
        PinTaskbarMenuItem.Visibility = Visibility.Collapsed;
        if (ItemsList.SelectedItems.Count == 1 && ItemsList.SelectedItem is DesktopShellNamespaceEntry entry)
        {
            OpenWithMenuItem.Visibility = ShellOpenWithPolicy.CanOpenWith(true, entry.IsFolder, true)
                ? Visibility.Visible
                : Visibility.Collapsed;
            var pinTarget = TaskbarPinCatalog.IsSupportedShellNamespaceTarget(entry.ParsingName)
                || TaskbarPinCatalog.IsSupportedTarget(entry.ParsingName, entry.IsFolder);
            var canPinStart = pinTarget && _pinStartItem is not null;
            var canPinTaskbar = pinTarget && _pinTaskbarItem is not null;
            PinStartMenuItem.Visibility = canPinStart ? Visibility.Visible : Visibility.Collapsed;
            PinStartMenuItem.IsEnabled = canPinStart && _isStartItemPinned?.Invoke(entry.ParsingName) != true;
            PinTaskbarMenuItem.Visibility = canPinTaskbar ? Visibility.Visible : Visibility.Collapsed;
            PinTaskbarMenuItem.IsEnabled = canPinTaskbar && _isTaskbarItemPinned?.Invoke(entry.ParsingName) != true;
            RenameMenuItem.IsEnabled = await CanRenameAsync(entry);
        }
        ShowMoreOptionsMenuItem.Header = hasSelection ? "Show more options" : "Show folder options";
        ShowMoreOptionsMenuItem.IsEnabled = true;
    }

    private static async Task<bool> CanRenameAsync(DesktopShellNamespaceEntry entry)
    {
        if (!entry.RenameCapabilityChecked)
        {
            var canRename = await Task.Run(() => NativeShellContextMenuService.CanRenameShellItem(entry.ParsingName));
            entry.SetRenameCapability(canRename);
        }
        return entry.CanRename;
    }

    private void Rename_Click(object sender, RoutedEventArgs e) => BeginRenameSelected();

    private async void OpenWith_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItems.Count != 1 || ItemsList.SelectedItem is not DesktopShellNamespaceEntry { IsFolder: false } entry)
            return;
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            await NativeShellContextMenuService.OpenWithShellItemAsync(owner, entry.ParsingName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open the Open with dialog", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PinStart_Click(object sender, RoutedEventArgs e)
    {
        if (_pinStartItem is null || ItemsList.SelectedItems.Count != 1 || ItemsList.SelectedItem is not DesktopShellNamespaceEntry entry) return;
        if (_pinStartItem(entry.ParsingName)) StatusText.Text = $"Pinned {entry.Name} to Start.";
    }

    private void PinTaskbar_Click(object sender, RoutedEventArgs e)
    {
        if (_pinTaskbarItem is null || ItemsList.SelectedItems.Count != 1 || ItemsList.SelectedItem is not DesktopShellNamespaceEntry entry) return;
        if (_pinTaskbarItem(entry.ParsingName)) StatusText.Text = $"Pinned {entry.Name} to the taskbar.";
    }

    private async void Copy_Click(object sender, RoutedEventArgs e) => await CopySelectedItemsAsync(cut: false);

    private async void Cut_Click(object sender, RoutedEventArgs e) => await CopySelectedItemsAsync(cut: true);

    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteIntoCurrentLocationAsync();

    private async void NewFolder_Click(object sender, RoutedEventArgs e) => await CreateFolderAsync();

    private async Task CreateFolderAsync()
    {
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            if (await NativeShellContextMenuService.CreateFolderInShellFolderAsync(owner, _location))
            {
                StatusText.Text = "Created a new folder.";
                await RefreshCurrentViewAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not create a new folder: {ex.Message}";
        }
    }

    private void BeginRenameSelected()
    {
        if (ItemsList.SelectedItems.Count != 1 || ItemsList.SelectedItem is not DesktopShellNamespaceEntry { CanRename: true } entry) return;
        entry.RenameText = entry.Name;
        entry.IsRenaming = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!entry.IsRenaming) return;
            var container = ItemsList.ItemContainerGenerator.ContainerFromItem(entry);
            var editor = container is null ? null : FindVisualDescendant<TextBox>(container);
            if (editor is null)
            {
                entry.IsRenaming = false;
                return;
            }
            editor.Focus();
            Keyboard.Focus(editor);
            editor.Select(0, ExplorerRenamePolicy.GetInitialSelectionLength(entry.Name, entry.IsFolder));
        }));
    }

    private async void RenameEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: DesktopShellNamespaceEntry entry }) await CommitRenameAsync(entry);
    }

    private async Task CommitRenameAsync(DesktopShellNamespaceEntry entry)
    {
        if (!entry.IsRenaming) return;
        var newName = entry.RenameText.Trim();
        entry.IsRenaming = false;
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(entry.Name, newName, StringComparison.Ordinal))
        {
            entry.RenameText = entry.Name;
            return;
        }

        try
        {
            await NativeShellContextMenuService.RenameShellItemAsync(entry.ParsingName, newName);
            await RefreshCurrentViewAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            entry.RenameText = entry.Name;
            MessageBox.Show(this, ex.Message, "Could not rename Shell item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var descendant = FindVisualDescendant<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private void ItemsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(ItemsList, source) is not ListViewItem item)
        {
            ItemsList.SelectedItems.Clear();
            return;
        }
        if (!item.IsSelected)
        {
            ItemsList.SelectedItems.Clear();
            item.IsSelected = true;
        }
        item.Focus();
    }

    private async void ShowMoreOptions_Click(object sender, RoutedEventArgs e)
    {
        await ShowShellContextMenuAsync();
    }

    private async Task ShowShellContextMenuAsync()
    {
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            var selection = ItemsList.SelectedItems.OfType<DesktopShellNamespaceEntry>().ToArray();
            if (selection.Length == 0)
                await NativeShellContextMenuService.ShowForShellFolderBackgroundAsync(owner, _location);
            else
                await NativeShellContextMenuService.ShowForShellItemsAsync(owner, selection.Select(entry => entry.ParsingName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open Windows' context menu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox { DataContext: DesktopShellNamespaceEntry editingEntry } && editingEntry.IsRenaming)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await CommitRenameAsync(editingEntry);
            }
            else if (e.Key == Key.Escape)
            {
                editingEntry.RenameText = editingEntry.Name;
                editingEntry.IsRenaming = false;
                e.Handled = true;
            }
            else return;
            return;
        }

        if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None && ItemsList.IsKeyboardFocusWithin &&
            ItemsList.SelectedItems.Count == 1 && ItemsList.SelectedItem is DesktopShellNamespaceEntry { IsRenaming: false } renameEntry)
            await CanRenameAsync(renameEntry);

        var keyboardAction = ShellNamespaceBrowserKeyboardPolicy.Resolve(
            e.Key, Keyboard.Modifiers, ItemsList.IsKeyboardFocusWithin, ItemsList.SelectedItems.Count > 0,
            ItemsList.SelectedItem is DesktopShellNamespaceEntry { CanRename: true }, e.SystemKey);
        if (keyboardAction == ShellNamespaceBrowserKeyboardAction.SelectAll)
        {
            ItemsList.SelectAll();
            e.Handled = true;
        }
        else if (keyboardAction == ShellNamespaceBrowserKeyboardAction.ClearSelection)
        {
            ItemsList.SelectedItems.Clear();
            e.Handled = true;
        }
        else if (keyboardAction == ShellNamespaceBrowserKeyboardAction.Copy)
        {
            await CopySelectedItemsAsync(cut: false);
            e.Handled = true;
        }
        else if (keyboardAction == ShellNamespaceBrowserKeyboardAction.Cut)
        {
            await CopySelectedItemsAsync(cut: true);
            e.Handled = true;
        }
        else if (keyboardAction == ShellNamespaceBrowserKeyboardAction.Paste)
        {
            await PasteIntoCurrentLocationAsync();
            e.Handled = true;
        }
        else if (keyboardAction == ShellNamespaceBrowserKeyboardAction.CreateFolder)
        {
            await CreateFolderAsync();
            e.Handled = true;
        }
        else if (keyboardAction == ShellNamespaceBrowserKeyboardAction.ShowContextMenu)
        {
            await ShowShellContextMenuAsync();
            e.Handled = true;
        }
        else if (keyboardAction == ShellNamespaceBrowserKeyboardAction.ShowProperties)
        {
            await ShowSelectedPropertiesAsync();
            e.Handled = true;
        }
        else if (keyboardAction == ShellNamespaceBrowserKeyboardAction.Delete)
        {
            await DeleteSelectedItemsAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (keyboardAction == ShellNamespaceBrowserKeyboardAction.Rename)
        {
            BeginRenameSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.F5 || e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = RefreshCurrentViewAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin && SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();
            if (_isSearchView) await NavigateAsync(_location, recordHistory: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Back && Keyboard.Modifiers == ModifierKeys.None)
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

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _sourceInitialized = true;
        SystemBackdropService.TryApplyMica(this);
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        if (_windowSource is not null)
        {
            _windowMessageHook = WindowMessageHook;
            _windowSource.AddHook(_windowMessageHook);
            _clipboardListenerRegistered = AddClipboardFormatListener(handle);
        }
        if (!_clipboardListenerRegistered)
            Trace.TraceWarning($"Could not monitor clipboard changes for Shell cut-state display: {Marshal.GetLastWin32Error()}");
        RegisterShellChangeNotifications(_location);
    }

    private void SetCutState(IEnumerable<DesktopShellNamespaceEntry> selectedItems, uint clipboardSequence)
    {
        if (!_clipboardListenerRegistered || NativeShellContextMenuService.ReadClipboardSequenceNumber() != clipboardSequence)
        {
            ClearCutState();
            return;
        }

        _cutParsingNames.Clear();
        foreach (var entry in selectedItems) _cutParsingNames.Add(entry.ParsingName);
        _cutClipboardSequence = clipboardSequence;
        ApplyCutState(ItemsList.Items.OfType<DesktopShellNamespaceEntry>());
    }

    private void ClearCutState()
    {
        _cutParsingNames.Clear();
        _cutClipboardSequence = 0;
        ApplyCutState(ItemsList.Items.OfType<DesktopShellNamespaceEntry>());
    }

    private void ApplyCutState(IEnumerable<DesktopShellNamespaceEntry> entries)
    {
        if (_cutParsingNames.Count > 0 && NativeShellContextMenuService.ReadClipboardSequenceNumber() != _cutClipboardSequence)
        {
            _cutParsingNames.Clear();
            _cutClipboardSequence = 0;
        }
        foreach (var entry in entries) entry.IsCut = _cutParsingNames.Contains(entry.ParsingName);
    }

    private void RefreshCutStateFromClipboard()
    {
        var clipboardSequence = NativeShellContextMenuService.ReadClipboardSequenceNumber();
        if (_cutParsingNames.Count > 0 && clipboardSequence == _cutClipboardSequence)
        {
            ApplyCutState(ItemsList.Items.OfType<DesktopShellNamespaceEntry>());
            return;
        }

        var parsingNames = NativeShellContextMenuService.ReadCutItemParsingNamesFromClipboard();
        if (parsingNames.Count == 0)
        {
            ClearCutState();
            return;
        }

        _cutParsingNames.Clear();
        foreach (var parsingName in parsingNames)
        {
            try
            {
                _cutParsingNames.Add(Path.IsPathFullyQualified(parsingName) ? Path.GetFullPath(parsingName) : parsingName);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                _cutParsingNames.Add(parsingName);
            }
        }
        _cutClipboardSequence = clipboardSequence;
        ApplyCutState(ItemsList.Items.OfType<DesktopShellNamespaceEntry>());
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == MessageClipboardUpdate)
            RefreshCutStateFromClipboard();
        return nint.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint window);
}
