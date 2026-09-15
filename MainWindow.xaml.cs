using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DesktopTuner;

public partial class MainWindow : Window
{
    private const int StartMenuHotkeyId = 0x5D01;
    private const int TaskbarAutoHideHotkeyId = 0x5D02;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
    private const int WM_APP_ACTIVATE_SETTINGS = 0x8000 + 0x451;
    private const int WM_COPYDATA = 0x004A;
    private const long WM_COPYDATA_OPEN_FOLDER = 0x44544E52;
    private const long WM_COPYDATA_OPEN_SHELL_LOCATION = 0x44544E53;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_WIN = 0x0008;
    private const uint VK_SPACE = 0x20;
    private const uint VK_T = 0x54;
    private const uint MOD_NOREPEAT = 0x4000;
    private readonly ProfileStore _profileStore = new();
    private readonly DesktopPreferencesStore _preferences = new();
    private readonly RegistrySettingsService _settings;
    private readonly Dictionary<string, ComboBox> _controls = [];
    private readonly Dictionary<string, int> _currentValues = [];
    private readonly Dictionary<string, int> _selectedValues = [];
    private readonly HashSet<string> _dirty = [];
    private string _activePage = "Overview";
    private HwndSource? _windowSource;
    private StartMenuWindow? _startMenuWindow;
    private ExplorerWindow? _explorerWindow;
    private ShellNamespaceBrowserWindow? _shellNamespaceBrowserWindow;
    private TaskbarDisplay? _startMenuDisplay;
    private readonly List<TaskbarWindow> _taskbarWindows = [];
    private nint _taskbarFocusReturnWindow;
    private readonly TaskbarWindowOrder _taskbarWindowOrder = new();
    private readonly ShowDesktopWindowService _showDesktopWindows = new();
    private readonly ForegroundWindowHistory _foregroundWindowHistory;
    private readonly NativeTaskbarVisibilityService _nativeTaskbarVisibility = new();
    private readonly DispatcherTimer _nativeTaskbarWatchTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _shellHostTrayRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _displayRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _shellHostHeartbeatTimer = new() { Interval = CustomShellPolicy.HostHeartbeatInterval };
    private WindowsKeyStartHook? _windowsKeyHook;
    private TaskbarEdge _taskbarEdge = TaskbarEdge.Bottom;
    private TaskbarSize _taskbarSize = TaskbarSize.Standard;
    private TaskbarStyle _taskbarLayout = TaskbarStyle.EdgeToEdge;
    private TaskbarVisualStyle _taskbarVisualStyle = TaskbarVisualStyle.Windows11;
    private TaskbarGroupingMode _taskbarGrouping = TaskbarGroupingMode.Always;
    private TaskbarButtonAlignment _taskbarButtonAlignment = TaskbarButtonAlignment.Center;
    private TaskbarIconSize _taskbarIconSize = TaskbarIconSize.Standard;
    private TaskbarButtonSpacing _taskbarButtonSpacing = TaskbarButtonSpacing.Standard;
    private TaskbarButtonEffect _taskbarButtonEffect = TaskbarButtonEffect.Accent;
    private TaskbarSystemButtonVisibility _taskbarSystemButtons = TaskbarSystemButtonVisibility.Default;
    private TaskbarWeatherSettings _taskbarWeather = new();
    private bool _taskbarShowLabels = true;
    private TaskbarLabelVisibility _taskbarLabelVisibility = TaskbarLabelVisibility.Always;
    private bool _taskbarLocked;
    private bool _updatingTaskbarClock;
    private bool _taskbarAutoHide;
    private bool _taskbarAutoHideWhenMaximized;
    private int _taskbarTransparency = 5;
    private bool _taskbarDynamicTransparency;
    private List<PinnedTaskbarApp> _pinnedApps = [];
    private List<AppEntry> _pinnedStartApps = [];
    private StartMenuPlacePreferences _startMenuPlaces = StartMenuPlaceCatalog.Normalize(null);
    private ControlPanelAppletPreferences _controlPanelApplets = ControlPanelAppletCatalog.Normalize(null);
    private bool _replaceWindowsKey;
    private bool _replaceWindowsKeyPreference;
    private bool _replaceExplorerShortcut;
    private StartMenuStyle _startMenuStyle = StartMenuStyle.Modern;
    private int _startRecentAppCount = 4;
    private bool _centerStartMenu;
    private bool _taskbarOnAllDisplays = true;
    private TaskbarWindowDisplayMode _taskbarWindowDisplayMode = TaskbarWindowDisplayMode.AllTaskbars;
    private bool _taskbarShowWindowsFromAllVirtualDesktops;
    private bool _replaceNativeTaskbar;
    private bool _startWithWindows;
    private bool _folderShellIntegrationEnabled;
    private readonly bool _startInBackground;
    private readonly bool _shellHostMode;
    private readonly bool _shellOverlayMode;
    private bool _shellHostReadySignaled;
    private bool _shellHostNativeTrayIntegrated;
    private bool _shellHostTaskbarNativeTrayIntegrated;
    private bool _closingTaskbars;
    private bool _reconcilingDisplayTopology;

    public MainWindow(bool startInBackground = false, bool shellHostMode = false, bool shellOverlayMode = false)
    {
        InitializeComponent();
        _foregroundWindowHistory = new ForegroundWindowHistory();
        _startInBackground = startInBackground;
        _shellHostMode = shellHostMode;
        _shellOverlayMode = shellOverlayMode;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        _settings = new RegistrySettingsService(_profileStore);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        _displayRefreshTimer.Tick += DisplayRefreshTimer_Tick;
        _shellHostTrayRefreshTimer.Tick += ShellHostTrayRefreshTimer_Tick;
        var desktopPreferences = _preferences.Load();
        _taskbarEdge = desktopPreferences.TaskbarEdge;
        _taskbarSize = desktopPreferences.TaskbarSize;
        _taskbarLayout = desktopPreferences.TaskbarLayout;
        _taskbarVisualStyle = desktopPreferences.TaskbarVisualStyle;
        _taskbarIconSize = desktopPreferences.TaskbarIconSize;
        _taskbarButtonSpacing = desktopPreferences.TaskbarButtonSpacing;
        _taskbarButtonEffect = desktopPreferences.TaskbarButtonEffect;
        _taskbarSystemButtons = TaskbarSystemButtonVisibility.Normalize(desktopPreferences.TaskbarSystemButtons);
        _taskbarWeather = TaskbarWeatherPolicy.Normalize(desktopPreferences.TaskbarWeather);
        _taskbarShowLabels = desktopPreferences.TaskbarShowLabels;
        _taskbarLabelVisibility = !desktopPreferences.TaskbarShowLabels && desktopPreferences.TaskbarLabelVisibility == TaskbarLabelVisibility.Always
            ? TaskbarLabelVisibility.Never : desktopPreferences.TaskbarLabelVisibility;
        _taskbarLocked = desktopPreferences.TaskbarLocked;
        _taskbarAutoHide = desktopPreferences.AutoHide;
        _taskbarAutoHideWhenMaximized = desktopPreferences.AutoHideWhenMaximized;
        _taskbarTransparency = desktopPreferences.TaskbarTransparency;
        _taskbarDynamicTransparency = desktopPreferences.TaskbarDynamicTransparency;
        _pinnedApps = desktopPreferences.PinnedApps ?? [];
        _pinnedStartApps = StartPinCatalog.Normalize(desktopPreferences.PinnedStartApps).ToList();
        _startMenuPlaces = StartMenuPlaceCatalog.Normalize(desktopPreferences.StartMenuPlaces);
        _controlPanelApplets = ControlPanelAppletCatalog.Normalize(desktopPreferences.ControlPanelApplets);
        _replaceWindowsKeyPreference = desktopPreferences.ReplaceWindowsKey;
        _replaceWindowsKey = shellHostMode || shellOverlayMode || desktopPreferences.ReplaceWindowsKey;
        _replaceExplorerShortcut = ShellHostLaunchPolicy.ShouldReplaceExplorerShortcut(shellHostMode, desktopPreferences.ReplaceExplorerShortcut);
        _startMenuStyle = desktopPreferences.StartMenuStyle;
        _startRecentAppCount = desktopPreferences.StartRecentAppCount;
        _centerStartMenu = desktopPreferences.CenterStartMenu;
        _taskbarOnAllDisplays = desktopPreferences.TaskbarOnAllDisplays;
        _taskbarWindowDisplayMode = desktopPreferences.TaskbarWindowDisplayMode;
        _taskbarShowWindowsFromAllVirtualDesktops = desktopPreferences.TaskbarShowWindowsFromAllVirtualDesktops;
        _replaceNativeTaskbar = desktopPreferences.ReplaceNativeTaskbar;
        _folderShellIntegrationEnabled = desktopPreferences.FolderShellIntegrationEnabled;
        _nativeTaskbarWatchTimer.Tick += (_, _) => MaintainNativeTaskbars();
        _startWithWindows = desktopPreferences.StartWithWindows;
        foreach (var setting in SettingsCatalog.All)
        {
            var value = _settings.Read(setting);
            _currentValues[setting.Id] = value;
            _selectedValues[setting.Id] = value;
        }
        DesktopTheme.Apply(_currentValues["explorer-app-mode"] == 0);
        _taskbarGrouping = (TaskbarGroupingMode)_currentValues["taskbar-combine"];
        _taskbarButtonAlignment = (TaskbarButtonAlignment)_currentValues["taskbar-alignment"];
        RenderPage("Overview");
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) RenderPage(page);
    }

    private void RenderPage(string page)
    {
        _activePage = page;
        _controls.Clear();
        PageContent.Children.Clear();
        Breadcrumb.Text = page switch { "Explorer" => "File Explorer", "Profiles" => "Profiles & restore", _ => page };

        if (page == "Overview") RenderOverview();
        else if (page == "Profiles") RenderProfiles();
        else if (page is "Start" or "Taskbar" or "Explorer") RenderSettingsPage(page);
        UpdateStatus();
    }

    private void RenderOverview()
    {
        var title = ThemedText("Make Windows feel like yours.", "DesktopPrimaryTextBrush");
        title.FontSize = 30;
        title.FontWeight = FontWeights.SemiBold;
        PageContent.Children.Add(title);
        var subtitle = ThemedText("Tune the Start menu, taskbar, and File Explorer in one place. Changes are per-user and reversible.", "DesktopMutedTextBrush");
        subtitle.FontSize = 14;
        subtitle.Margin = new Thickness(0, 8, 0, 24);
        PageContent.Children.Add(subtitle);

        var hero = new Border { Background = Brush("#232A3D"), CornerRadius = new CornerRadius(14), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 22) };
        var heroStack = new StackPanel();
        var heroLabel = ThemedText("YOUR WINDOWS, RECLAIMED", "DesktopAccentTextBrush");
        heroLabel.FontSize = 11;
        heroLabel.FontWeight = FontWeights.Bold;
        heroStack.Children.Add(heroLabel);
        heroStack.Children.Add(new TextBlock { Text = "A calmer desktop starts with the details.", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 9, 0, 4) });
        heroStack.Children.Add(new TextBlock { Text = "Choose the settings that fit your workflow, save them as a profile, and bring them back whenever you need.", FontSize = 13, Foreground = Brush("#C1C8D7"), TextWrapping = TextWrapping.Wrap });
        hero.Child = heroStack;
        PageContent.Children.Add(hero);

        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 18) };
        AddModuleCard(grid, "Start menu", "Open apps with a searchable launcher and choose how Windows handles recent activity.", "Start", "01");
        AddModuleCard(grid, "Taskbar", "A live app-switching bar plus taskbar alignment and grouping preferences.", "Taskbar", "02");
        AddModuleCard(grid, "File Explorer", "Useful defaults for everyday file browsing.", "Explorer", "03");
        PageContent.Children.Add(grid);

        var note = Card();
        var noteStack = new StackPanel();
        noteStack.Children.Add(new TextBlock { Text = "A note about compatibility", FontWeight = FontWeights.SemiBold, FontSize = 14 });
        var noteDescription = ThemedText("Windows can change shell behavior between releases. Taskbar controls are marked experimental and may need an Explorer restart or sign-out on some builds. This app never injects code into Explorer.", "DesktopMutedTextBrush");
        noteDescription.FontSize = 12;
        noteDescription.TextWrapping = TextWrapping.Wrap;
        noteDescription.Margin = new Thickness(0, 6, 0, 0);
        noteStack.Children.Add(noteDescription);
        note.Child = noteStack;
        PageContent.Children.Add(note);
    }

    private void AddModuleCard(Panel parent, string title, string description, string page, string number)
    {
        var card = Card();
        card.Margin = new Thickness(0, 0, 12, 0);
        var stack = new StackPanel();
        var numberText = ThemedText(number, "DesktopAccentTextBrush");
        numberText.FontSize = 11;
        numberText.FontWeight = FontWeights.Bold;
        stack.Children.Add(numberText);
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 5) });
        var descriptionText = ThemedText(description, "DesktopMutedTextBrush");
        descriptionText.FontSize = 12;
        descriptionText.TextWrapping = TextWrapping.Wrap;
        descriptionText.MinHeight = 48;
        stack.Children.Add(descriptionText);
        var button = new Button { Content = "Customize  →", Tag = page, Style = (Style)FindResource("SecondaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 15, 0, 0), Padding = new Thickness(12, 8, 12, 8) };
        button.Click += Navigate_Click;
        stack.Children.Add(button);
        card.Child = stack;
        parent.Children.Add(card);
    }

    private void RenderSettingsPage(string section)
    {
        var settings = SettingsCatalog.All.Where(setting => setting.Section == section).ToList();
        var title = section == "Explorer" ? "File Explorer" : section == "Start" ? "Start menu" : "Taskbar";
        AddPageHeading(title, section == "Start"
            ? "Shape your Start experience and decide how much recent activity Windows remembers."
            : section == "Taskbar"
                ? "Set the taskbar up for your flow. These options use Windows user settings and can vary by build."
                : "Set the defaults that make browsing your files feel more natural.");

        if (section == "Start")
        {
            var launchButton = new Button { Content = "Open Desktop Tuner Start menu", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) };
            launchButton.Click += (_, _) => ShowStartMenu();
            PageContent.Children.Add(launchButton);
            var menuStyleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            menuStyleRow.Children.Add(new TextBlock { Text = "Start menu style", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var menuStyleSelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            menuStyleSelector.Items.Add(new ComboBoxItem { Content = "Modern", Tag = StartMenuStyle.Modern });
            menuStyleSelector.Items.Add(new ComboBoxItem { Content = "Classic", Tag = StartMenuStyle.Classic });
            menuStyleSelector.Items.Add(new ComboBoxItem { Content = "Compact", Tag = StartMenuStyle.Compact });
            menuStyleSelector.Items.Add(new ComboBoxItem { Content = "Windows 7 inspired", Tag = StartMenuStyle.Windows7 });
            menuStyleSelector.Items.Add(new ComboBoxItem { Content = "Windows 8 inspired tiles", Tag = StartMenuStyle.Windows8 });
            menuStyleSelector.Items.Add(new ComboBoxItem { Content = "Windows 10 inspired tiles", Tag = StartMenuStyle.Windows10 });
            menuStyleSelector.SelectedIndex = (int)_startMenuStyle;
            menuStyleSelector.SelectionChanged += (_, _) =>
            {
                if (menuStyleSelector.SelectedItem is not ComboBoxItem { Tag: StartMenuStyle style }) return;
                _startMenuStyle = style;
                SaveDesktopPreferences();
                if (_startMenuWindow?.IsVisible == true) PositionStartMenuWindow();
            };
            menuStyleRow.Children.Add(menuStyleSelector);
            PageContent.Children.Add(menuStyleRow);
            var centerStartMenu = new CheckBox { Content = "Center the Start menu along the taskbar edge", IsChecked = _centerStartMenu, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            centerStartMenu.Checked += (_, _) => SetStartMenuCentered(true);
            centerStartMenu.Unchecked += (_, _) => SetStartMenuCentered(false);
            PageContent.Children.Add(centerStartMenu);
            var recentAppsRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            recentAppsRow.Children.Add(new TextBlock { Text = "Recent apps", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var recentAppsSelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var count in Enumerable.Range(0, StartRecentAppsStore.MaximumEntries + 1))
                recentAppsSelector.Items.Add(new ComboBoxItem { Content = count == 0 ? "Off" : $"{count} app{(count == 1 ? "" : "s")}", Tag = count });
            recentAppsSelector.SelectedItem = recentAppsSelector.Items.Cast<ComboBoxItem>().First(item => (int)item.Tag == _startRecentAppCount);
            recentAppsSelector.SelectionChanged += (_, _) =>
            {
                if (recentAppsSelector.SelectedItem is not ComboBoxItem { Tag: int count }) return;
                _startRecentAppCount = count;
                SaveDesktopPreferences();
                _startMenuWindow?.SetRecentAppCount(count);
            };
            recentAppsRow.Children.Add(recentAppsSelector);
            PageContent.Children.Add(recentAppsRow);
            var replaceStart = new CheckBox
            {
                Content = _shellHostMode || _shellOverlayMode
                    ? "Use Desktop Tuner Start and taskbar shortcuts for the Windows key while this desktop is active"
                    : "Use Desktop Tuner Start and taskbar shortcuts for the Windows key while this app is running",
                IsChecked = _replaceWindowsKey,
                IsEnabled = !_shellHostMode && !_shellOverlayMode,
                Margin = new Thickness(0, 0, 0, 16),
                FontSize = 13
            };
            replaceStart.Checked += (_, _) => ToggleWindowsKeyReplacement(replaceStart, true);
            replaceStart.Unchecked += (_, _) => ToggleWindowsKeyReplacement(replaceStart, false);
            PageContent.Children.Add(replaceStart);
            var info = InfoCard("Windows-key integration", _shellHostMode || _shellOverlayMode
                ? "Desktop Tuner Start receives the Windows key while the Desktop Tuner desktop is active. Other Win+key shortcuts such as Win+R continue to Windows."
                : "When enabled, tapping either Windows key opens Desktop Tuner Start. While a Desktop Tuner taskbar is running, Win+1 through Win+9 activate a pin, Win+Ctrl+1 through Win+Ctrl+9 activate its most recently active window, and Win+Shift+1 through Win+Shift+9 launch a new instance; Ctrl+Win+Shift+1 through Ctrl+Win+Shift+9 launch an elevated instance where supported. Other Win+key shortcuts such as Win+R continue to Windows. Turn this off at any time to restore native Start and taskbar shortcuts.");
            PageContent.Children.Add(info);

            AddPageHeading("System places", "Choose which shortcuts appear in More places and set their order.");
            var placesPanel = new StackPanel();
            void RefreshPlacesPanel()
            {
                placesPanel.Children.Clear();
                var visible = _startMenuPlaces.Visible!.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var placeId in _startMenuPlaces.Order!)
                {
                    var place = StartMenuPlaceCatalog.AllPlaces.Single(item => string.Equals(item.Id, placeId, StringComparison.OrdinalIgnoreCase));
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var checkBox = new CheckBox { Content = place.Label, IsChecked = visible.Contains(place.Id), VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 6, 8, 6) };
                    checkBox.Checked += (_, _) => SavePlaceVisibility(place.Id, true, RefreshPlacesPanel);
                    checkBox.Unchecked += (_, _) => SavePlaceVisibility(place.Id, false, RefreshPlacesPanel);
                    Grid.SetColumn(checkBox, 0);
                    row.Children.Add(checkBox);
                    var reorder = new StackPanel { Orientation = Orientation.Horizontal };
                    var index = _startMenuPlaces.Order.IndexOf(place.Id);
                    var up = new Button { Content = "↑", Tag = place.Id, ToolTip = $"Move {place.Label} up", IsEnabled = index > 0, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2) };
                    var down = new Button { Content = "↓", Tag = place.Id, ToolTip = $"Move {place.Label} down", IsEnabled = index < _startMenuPlaces.Order.Count - 1, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2) };
                    up.Click += (_, _) => MoveStartPlace(place.Id, -1, RefreshPlacesPanel);
                    down.Click += (_, _) => MoveStartPlace(place.Id, 1, RefreshPlacesPanel);
                    reorder.Children.Add(up);
                    reorder.Children.Add(down);
                    Grid.SetColumn(reorder, 1);
                    row.Children.Add(reorder);
                    placesPanel.Children.Add(row);
                }
            }
            RefreshPlacesPanel();
            PageContent.Children.Add(new Border { Background = (Brush)FindResource("DesktopSurfaceBrush"), BorderBrush = (Brush)FindResource("DesktopBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = placesPanel });

            AddPageHeading("Control Panel applets", "Choose which shortcuts appear in the Control Panel flyout and set their order.");
            var appletsPanel = new StackPanel();
            void RefreshControlPanelAppletsPanel()
            {
                appletsPanel.Children.Clear();
                var visible = _controlPanelApplets.Visible!.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var availableApplets = ControlPanelAppletCatalog.GetAvailableApplets(Environment.SystemDirectory)
                    .ToDictionary(applet => applet.Id, StringComparer.OrdinalIgnoreCase);
                foreach (var appletId in _controlPanelApplets.Order!)
                {
                    if (!availableApplets.TryGetValue(appletId, out var applet)) continue;
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var checkBox = new CheckBox { Content = applet.Label, IsChecked = visible.Contains(applet.Id), VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 6, 8, 6) };
                    checkBox.Checked += (_, _) => SaveControlPanelAppletVisibility(applet.Id, true, RefreshControlPanelAppletsPanel);
                    checkBox.Unchecked += (_, _) => SaveControlPanelAppletVisibility(applet.Id, false, RefreshControlPanelAppletsPanel);
                    Grid.SetColumn(checkBox, 0);
                    row.Children.Add(checkBox);
                    var reorder = new StackPanel { Orientation = Orientation.Horizontal };
                    var index = _controlPanelApplets.Order.IndexOf(applet.Id);
                    var up = new Button { Content = "↑", ToolTip = $"Move {applet.Label} up", IsEnabled = index > 0, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2) };
                    var down = new Button { Content = "↓", ToolTip = $"Move {applet.Label} down", IsEnabled = index < _controlPanelApplets.Order.Count - 1, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2) };
                    up.Click += (_, _) => MoveControlPanelApplet(applet.Id, -1, RefreshControlPanelAppletsPanel);
                    down.Click += (_, _) => MoveControlPanelApplet(applet.Id, 1, RefreshControlPanelAppletsPanel);
                    reorder.Children.Add(up);
                    reorder.Children.Add(down);
                    Grid.SetColumn(reorder, 1);
                    row.Children.Add(reorder);
                    appletsPanel.Children.Add(row);
                }
            }
            RefreshControlPanelAppletsPanel();
            PageContent.Children.Add(new Border { Background = (Brush)FindResource("DesktopSurfaceBrush"), BorderBrush = (Brush)FindResource("DesktopBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = appletsPanel });
        }
        else if (section == "Explorer")
        {
            var explorerButton = new Button { Content = "Open Desktop Tuner Explorer", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) };
            explorerButton.Click += (_, _) => OpenExplorer();
            PageContent.Children.Add(explorerButton);
            var replaceExplorerShortcut = new CheckBox
            {
                Content = _shellHostMode ? "Open Desktop Tuner Explorer with Win+E in shell replacement mode" : "Open Desktop Tuner Explorer with Win+E while this app is running",
                IsChecked = _replaceExplorerShortcut,
                IsEnabled = !_shellHostMode,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 13
            };
            replaceExplorerShortcut.Checked += (_, _) => ToggleExplorerShortcutReplacement(replaceExplorerShortcut, true);
            replaceExplorerShortcut.Unchecked += (_, _) => ToggleExplorerShortcutReplacement(replaceExplorerShortcut, false);
            PageContent.Children.Add(replaceExplorerShortcut);
            PageContent.Children.Add(InfoCard("Explorer shortcut integration", "When enabled, Win+E opens the companion Explorer and Windows Explorer stays available from the taskbar and other apps. The shortcut returns to Windows when Desktop Tuner closes."));
            var folderShellIntegration = new CheckBox { Content = "Add “Open with Desktop Tuner” to folder context menus", IsChecked = _folderShellIntegrationEnabled, Margin = new Thickness(0, 0, 0, 12), FontSize = 13 };
            folderShellIntegration.Checked += (_, _) => SetFolderShellIntegration(folderShellIntegration, true);
            folderShellIntegration.Unchecked += (_, _) => SetFolderShellIntegration(folderShellIntegration, false);
            PageContent.Children.Add(folderShellIntegration);
            PageContent.Children.Add(InfoCard("Folder context menus", "Adds per-user commands for filesystem folders and empty-folder backgrounds. The command opens the selected location in Desktop Tuner Explorer and leaves Windows' default folder handler unchanged. Windows 11 may place these commands under Show more options."));
            PageContent.Children.Add(InfoCard("Classic browsing tools", "The companion Explorer includes a command strip, quick access locations, current-folder search, and a bottom details pane. Double-click folders to browse or files to open them with their default app."));
        }
        if (section == "Taskbar")
        {
            var edgeRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            edgeRow.Children.Add(new TextBlock { Text = "Overlay position", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var edgeSelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            edgeSelector.Items.Add(new ComboBoxItem { Content = "Bottom edge", Tag = TaskbarEdge.Bottom });
            edgeSelector.Items.Add(new ComboBoxItem { Content = "Top edge", Tag = TaskbarEdge.Top });
            edgeSelector.Items.Add(new ComboBoxItem { Content = "Left edge", Tag = TaskbarEdge.Left });
            edgeSelector.Items.Add(new ComboBoxItem { Content = "Right edge", Tag = TaskbarEdge.Right });
            edgeSelector.SelectedIndex = (int)_taskbarEdge;
            edgeSelector.SelectionChanged += (_, _) =>
            {
                if (edgeSelector.SelectedItem is not ComboBoxItem { Tag: TaskbarEdge edge }) return;
                _taskbarEdge = edge;
                SaveDesktopPreferences();
            };
            edgeRow.Children.Add(edgeSelector);
            PageContent.Children.Add(edgeRow);

            var densityRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            densityRow.Children.Add(new TextBlock { Text = "Taskbar size", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var sizeSelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            sizeSelector.Items.Add(new ComboBoxItem { Content = "Small", Tag = TaskbarSize.Small });
            sizeSelector.Items.Add(new ComboBoxItem { Content = "Standard", Tag = TaskbarSize.Standard });
            sizeSelector.Items.Add(new ComboBoxItem { Content = "Large", Tag = TaskbarSize.Large });
            sizeSelector.SelectedIndex = (int)_taskbarSize;
            sizeSelector.SelectionChanged += (_, _) =>
            {
                if (sizeSelector.SelectedItem is not ComboBoxItem { Tag: TaskbarSize size }) return;
                _taskbarSize = size;
                SaveDesktopPreferences();
            };
            densityRow.Children.Add(sizeSelector);
            PageContent.Children.Add(densityRow);

            var iconSizeRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            iconSizeRow.Children.Add(new TextBlock { Text = "Button icon size", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var iconSizeSelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            iconSizeSelector.Items.Add(new ComboBoxItem { Content = "Small", Tag = TaskbarIconSize.Small });
            iconSizeSelector.Items.Add(new ComboBoxItem { Content = "Standard", Tag = TaskbarIconSize.Standard });
            iconSizeSelector.Items.Add(new ComboBoxItem { Content = "Large", Tag = TaskbarIconSize.Large });
            iconSizeSelector.SelectedIndex = (int)_taskbarIconSize;
            iconSizeSelector.SelectionChanged += (_, _) =>
            {
                if (iconSizeSelector.SelectedItem is not ComboBoxItem { Tag: TaskbarIconSize size }) return;
                _taskbarIconSize = size;
                SaveDesktopPreferences();
            };
            iconSizeRow.Children.Add(iconSizeSelector);
            PageContent.Children.Add(iconSizeRow);

            var buttonEffectRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            buttonEffectRow.Children.Add(new TextBlock { Text = "App button highlight", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var buttonEffectSelector = new ComboBox { Width = 230, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            buttonEffectSelector.Items.Add(new ComboBoxItem { Content = "Windows accent", Tag = TaskbarButtonEffect.Accent });
            buttonEffectSelector.Items.Add(new ComboBoxItem { Content = "Aura (app icon color)", Tag = TaskbarButtonEffect.Aura });
            buttonEffectSelector.Items.Add(new ComboBoxItem { Content = "Dynamic Aura (follows pointer)", Tag = TaskbarButtonEffect.DynamicAura });
            buttonEffectSelector.SelectedItem = buttonEffectSelector.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (TaskbarButtonEffect)item.Tag == _taskbarButtonEffect);
            buttonEffectSelector.SelectionChanged += (_, _) =>
            {
                if (buttonEffectSelector.SelectedItem is not ComboBoxItem { Tag: TaskbarButtonEffect effect }) return;
                _taskbarButtonEffect = effect;
                SaveDesktopPreferences();
            };
            buttonEffectRow.Children.Add(buttonEffectSelector);
            PageContent.Children.Add(buttonEffectRow);

            var spacingRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            spacingRow.Children.Add(new TextBlock { Text = "Button spacing", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var spacingSelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            spacingSelector.Items.Add(new ComboBoxItem { Content = "Compact", Tag = TaskbarButtonSpacing.Compact });
            spacingSelector.Items.Add(new ComboBoxItem { Content = "Standard", Tag = TaskbarButtonSpacing.Standard });
            spacingSelector.Items.Add(new ComboBoxItem { Content = "Relaxed", Tag = TaskbarButtonSpacing.Relaxed });
            spacingSelector.SelectedIndex = (int)_taskbarButtonSpacing;
            spacingSelector.SelectionChanged += (_, _) =>
            {
                if (spacingSelector.SelectedItem is not ComboBoxItem { Tag: TaskbarButtonSpacing spacing }) return;
                _taskbarButtonSpacing = spacing;
                SaveDesktopPreferences();
            };
            spacingRow.Children.Add(spacingSelector);
            PageContent.Children.Add(spacingRow);

            var labelsRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            labelsRow.Children.Add(new TextBlock { Text = "Taskbar app labels", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var labelsSelector = new ComboBox { Width = 210, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            labelsSelector.Items.Add(new ComboBoxItem { Content = "Always", Tag = TaskbarLabelVisibility.Always });
            labelsSelector.Items.Add(new ComboBoxItem { Content = "When taskbar is full", Tag = TaskbarLabelVisibility.WhenFull });
            labelsSelector.Items.Add(new ComboBoxItem { Content = "Never", Tag = TaskbarLabelVisibility.Never });
            labelsSelector.SelectedIndex = (int)_taskbarLabelVisibility;
            labelsSelector.SelectionChanged += (_, _) =>
            {
                if (labelsSelector.SelectedItem is not ComboBoxItem { Tag: TaskbarLabelVisibility visibility }) return;
                _taskbarLabelVisibility = visibility;
                _taskbarShowLabels = visibility != TaskbarLabelVisibility.Never;
                SaveDesktopPreferences();
            };
            labelsRow.Children.Add(labelsSelector);
            PageContent.Children.Add(labelsRow);

            AddPageHeading("Taskbar weather", "Show current conditions for a city or postal code you choose. Your location is sent to Open-Meteo only when you search or enable weather.");
            var weatherLocationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var weatherQuery = new TextBox
            {
                Text = _taskbarWeather.LocationQuery,
                Width = 250,
                Height = 36,
                MaxLength = 120,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "City or postal code, optionally followed by a region or country"
            };
            weatherLocationRow.Children.Add(weatherQuery);
            var weatherSearch = new Button { Content = "Find location", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(8, 0, 0, 0) };
            weatherLocationRow.Children.Add(weatherSearch);
            PageContent.Children.Add(weatherLocationRow);

            var weatherLocations = new List<TaskbarWeatherLocation>();
            var weatherLocationSelector = new ComboBox
            {
                Width = 360,
                Height = 36,
                DisplayMemberPath = nameof(TaskbarWeatherLocation.Name),
                ItemsSource = weatherLocations,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
                ToolTip = "Choose the matching city and region"
            };
            if (_taskbarWeather.Latitude is { } savedLatitude && _taskbarWeather.Longitude is { } savedLongitude)
            {
                var savedLocation = new TaskbarWeatherLocation(_taskbarWeather.LocationName, savedLatitude, savedLongitude);
                weatherLocations.Add(savedLocation);
                weatherLocationSelector.ItemsSource = null;
                weatherLocationSelector.ItemsSource = weatherLocations;
                weatherLocationSelector.SelectedIndex = 0;
            }
            PageContent.Children.Add(weatherLocationSelector);

            weatherSearch.Click += async (_, _) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(weatherQuery.Text))
                    {
                        MessageBox.Show(this, "Enter a city or postal code to search.", "Choose a weather location", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var locations = await TaskbarWeatherService.SearchLocationsAsync(weatherQuery.Text);
                    if (locations.Count == 0)
                    {
                        MessageBox.Show(this, "No matching locations were found. Add a region or country and search again.", "Weather location not found", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    weatherLocationSelector.ItemsSource = locations;
                    weatherLocationSelector.SelectedIndex = locations.Count == 1 ? 0 : -1;
                    if (locations.Count > 1) weatherLocationSelector.IsDropDownOpen = true;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException or InvalidOperationException)
                {
                    MessageBox.Show(this, $"Could not search for that location. Check your connection and try again.{Environment.NewLine}{ex.Message}", "Weather location search failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            var weatherEnabled = new CheckBox
            {
                Content = "Show weather on the taskbar",
                IsChecked = _taskbarWeather.Enabled,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 13
            };
            PageContent.Children.Add(weatherEnabled);
            var saveWeather = new Button { Content = "Save weather settings", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            saveWeather.Click += (_, _) =>
            {
                var location = weatherLocationSelector.SelectedItem as TaskbarWeatherLocation;
                if (weatherEnabled.IsChecked == true && location is null)
                {
                    MessageBox.Show(this, "Search for and select a city before enabling taskbar weather.", "Choose a weather location", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _taskbarWeather = TaskbarWeatherPolicy.Normalize(new TaskbarWeatherSettings(
                    weatherEnabled.IsChecked == true,
                    weatherQuery.Text,
                    location?.Name ?? _taskbarWeather.LocationName,
                    location?.Latitude ?? _taskbarWeather.Latitude,
                    location?.Longitude ?? _taskbarWeather.Longitude));
                SaveDesktopPreferences();
            };
            PageContent.Children.Add(saveWeather);
            PageContent.Children.Add(InfoCard("Weather data and privacy", "Open-Meteo weather and GeoNames location data are used for this optional feature. Searching sends your search text to Open-Meteo; enabled weather sends the selected coordinates for a forecast. Weather stays off until you turn it on. Data attribution: Open-Meteo and GeoNames. The public endpoint is for non-commercial use; commercial deployments need an appropriately licensed endpoint."));

            AddPageHeading("System buttons", "Choose which controls appear when Desktop Tuner draws the system area. If the native Windows notification area stays exposed, its tray and clock remain in place. Battery appears only in replacement mode when Windows reports a battery.");
            var systemButtonsPanel = new StackPanel();
            foreach (var option in TaskbarSystemButtonVisibility.Options)
            {
                var checkBox = new CheckBox
                {
                    Content = option.Label,
                    IsChecked = _taskbarSystemButtons.IsVisible(option.Button),
                    ToolTip = option.Description,
                    Margin = new Thickness(0, 0, 0, 8),
                    FontSize = 13
                };
                checkBox.Checked += (_, _) => SetTaskbarSystemButton(option.Button, true);
                checkBox.Unchecked += (_, _) => SetTaskbarSystemButton(option.Button, false);
                systemButtonsPanel.Children.Add(checkBox);
            }
            PageContent.Children.Add(new Border
            {
                Background = (Brush)FindResource("DesktopSurfaceBrush"),
                BorderBrush = (Brush)FindResource("DesktopBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 16),
                Child = systemButtonsPanel
            });

            var showClockSeconds = new CheckBox
            {
                Content = "Show seconds in the taskbar clock",
                IsChecked = TaskbarClockPolicy.ShouldShowSeconds(),
                ToolTip = "Uses the Windows Explorer taskbar clock preference for both the native and replacement taskbars.",
                Margin = new Thickness(0, 0, 0, 16),
                FontSize = 13
            };
            showClockSeconds.Checked += (_, _) => { if (!_updatingTaskbarClock) SetTaskbarClockSeconds(showClockSeconds, true); };
            showClockSeconds.Unchecked += (_, _) => { if (!_updatingTaskbarClock) SetTaskbarClockSeconds(showClockSeconds, false); };
            PageContent.Children.Add(showClockSeconds);

            var styleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            var visualStyleSelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            visualStyleSelector.Items.Add(new ComboBoxItem { Content = "Windows 11", Tag = TaskbarVisualStyle.Windows11 });
            visualStyleSelector.Items.Add(new ComboBoxItem { Content = "Windows 10", Tag = TaskbarVisualStyle.Windows10 });
            visualStyleSelector.Items.Add(new ComboBoxItem { Content = "Windows 7-inspired Aero", Tag = TaskbarVisualStyle.Windows7 });
            visualStyleSelector.SelectedItem = visualStyleSelector.Items.Cast<ComboBoxItem>().First(item => item.Tag is TaskbarVisualStyle visualStyle && visualStyle == _taskbarVisualStyle);
            visualStyleSelector.SelectionChanged += (_, _) =>
            {
                if (visualStyleSelector.SelectedItem is not ComboBoxItem { Tag: TaskbarVisualStyle visualStyle }) return;
                _taskbarVisualStyle = visualStyle;
                SaveDesktopPreferences();
            };
            var visualStyleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            visualStyleRow.Children.Add(new TextBlock { Text = "Taskbar visual style", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            visualStyleRow.Children.Add(visualStyleSelector);
            PageContent.Children.Add(visualStyleRow);

            styleRow.Children.Add(new TextBlock { Text = "Taskbar style", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var styleSelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            styleSelector.Items.Add(new ComboBoxItem { Content = "Full edge", Tag = TaskbarStyle.EdgeToEdge });
            styleSelector.Items.Add(new ComboBoxItem { Content = "Floating", Tag = TaskbarStyle.Floating });
            styleSelector.Items.Add(new ComboBoxItem { Content = "Segmented", Tag = TaskbarStyle.Segmented });
            styleSelector.Items.Add(new ComboBoxItem { Content = "Apps dock", Tag = TaskbarStyle.DockLike });
            styleSelector.SelectedIndex = (int)_taskbarLayout;
            styleSelector.SelectionChanged += (_, _) =>
            {
                if (styleSelector.SelectedItem is not ComboBoxItem { Tag: TaskbarStyle style }) return;
                _taskbarLayout = style;
                SaveDesktopPreferences();
            };
            styleRow.Children.Add(styleSelector);
            PageContent.Children.Add(styleRow);

            var transparencyRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            transparencyRow.Children.Add(new TextBlock { Text = "Taskbar transparency", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var transparencySelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var transparency in new[] { 0, 5, 15, 30, 50, 70 })
                transparencySelector.Items.Add(new ComboBoxItem { Content = $"{transparency}%", Tag = transparency });
            transparencySelector.SelectedItem = transparencySelector.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (int)item.Tag == _taskbarTransparency);
            if (transparencySelector.SelectedIndex < 0) transparencySelector.SelectedIndex = 1;
            transparencySelector.SelectionChanged += (_, _) =>
            {
                if (transparencySelector.SelectedItem is not ComboBoxItem { Tag: int transparency }) return;
                _taskbarTransparency = transparency;
                SaveDesktopPreferences();
            };
            transparencyRow.Children.Add(transparencySelector);
            PageContent.Children.Add(transparencyRow);

            var dynamicTransparency = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = "Use selected transparency while a maximized window covers this display; keep the taskbar solid on the desktop",
                    TextWrapping = TextWrapping.Wrap
                },
                IsChecked = _taskbarDynamicTransparency,
                Margin = new Thickness(0, 0, 0, 16),
                FontSize = 13
            };
            dynamicTransparency.Checked += (_, _) => { _taskbarDynamicTransparency = true; SaveDesktopPreferences(); };
            dynamicTransparency.Unchecked += (_, _) => { _taskbarDynamicTransparency = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(dynamicTransparency);

            var autoHide = new CheckBox { Content = "Automatically hide the custom taskbar", IsChecked = _taskbarAutoHide, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            autoHide.Checked += (_, _) => { _taskbarAutoHide = true; SaveDesktopPreferences(); };
            autoHide.Unchecked += (_, _) => { _taskbarAutoHide = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(autoHide);
            var autoHideMaximized = new CheckBox { Content = "Hide the taskbar when a maximized app covers this display", IsChecked = _taskbarAutoHideWhenMaximized, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            autoHideMaximized.Checked += (_, _) => { _taskbarAutoHideWhenMaximized = true; SaveDesktopPreferences(); };
            autoHideMaximized.Unchecked += (_, _) => { _taskbarAutoHideWhenMaximized = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(autoHideMaximized);
            var lockTaskbar = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = "Lock the taskbar (disable pin and window reordering)",
                    TextWrapping = TextWrapping.Wrap
                },
                IsChecked = _taskbarLocked,
                Margin = new Thickness(0, 0, 0, 16),
                FontSize = 13
            };
            lockTaskbar.Checked += (_, _) => { _taskbarLocked = true; SaveDesktopPreferences(); };
            lockTaskbar.Unchecked += (_, _) => { _taskbarLocked = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(lockTaskbar);
            var allDisplays = new CheckBox { Content = "Show the custom taskbar on all displays", IsChecked = _taskbarOnAllDisplays, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            allDisplays.Checked += (_, _) => { _taskbarOnAllDisplays = true; SaveDesktopPreferences(); };
            allDisplays.Unchecked += (_, _) => { _taskbarOnAllDisplays = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(allDisplays);
            var appDisplayRow = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            appDisplayRow.Children.Add(new TextBlock { Text = "Show running apps on", FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
            var appDisplaySelector = new ComboBox { MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left };
            appDisplaySelector.Items.Add(new ComboBoxItem { Content = "All taskbars", Tag = TaskbarWindowDisplayMode.AllTaskbars });
            appDisplaySelector.Items.Add(new ComboBoxItem { Content = "The taskbar where the window is open", Tag = TaskbarWindowDisplayMode.TaskbarOnWhichWindowIsOpen });
            appDisplaySelector.Items.Add(new ComboBoxItem { Content = "The taskbar where the window is open and the primary taskbar", Tag = TaskbarWindowDisplayMode.PrimaryAndTaskbarOnWhichWindowIsOpen });
            appDisplaySelector.SelectedItem = appDisplaySelector.Items.Cast<ComboBoxItem>().First(item => item.Tag is TaskbarWindowDisplayMode mode && mode == _taskbarWindowDisplayMode);
            appDisplaySelector.SelectionChanged += (_, _) =>
            {
                if (appDisplaySelector.SelectedItem is not ComboBoxItem { Tag: TaskbarWindowDisplayMode mode }) return;
                _taskbarWindowDisplayMode = mode;
                SaveDesktopPreferences();
            };
            appDisplayRow.Children.Add(appDisplaySelector);
            PageContent.Children.Add(appDisplayRow);
            var allVirtualDesktops = new CheckBox
            {
                Content = "Show open windows from all virtual desktops",
                IsChecked = _taskbarShowWindowsFromAllVirtualDesktops,
                Margin = new Thickness(0, 0, 0, 16),
                FontSize = 13
            };
            allVirtualDesktops.Checked += (_, _) => { _taskbarShowWindowsFromAllVirtualDesktops = true; SaveDesktopPreferences(); };
            allVirtualDesktops.Unchecked += (_, _) => { _taskbarShowWindowsFromAllVirtualDesktops = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(allVirtualDesktops);
            var replaceNativeTaskbar = new CheckBox
            {
                Content = _shellOverlayMode
                    ? "Overlay Explorer taskbars while preserving its notification area on supported layouts"
                    : "Replace the Windows taskbar while Desktop Tuner is running (experimental)",
                IsChecked = _shellOverlayMode || _replaceNativeTaskbar,
                IsEnabled = !_shellOverlayMode,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 13
            };
            replaceNativeTaskbar.Checked += (_, _) =>
            {
                _replaceNativeTaskbar = true;
                SaveDesktopPreferences();
            };
            replaceNativeTaskbar.Unchecked += (_, _) =>
            {
                _replaceNativeTaskbar = false;
                SaveDesktopPreferences();
            };
            PageContent.Children.Add(replaceNativeTaskbar);
            PageContent.Children.Add(new TextBlock
            {
                Text = _shellOverlayMode
                    ? "Desktop Tuner covers the main taskbar controls on every display while Explorer stays running behind it. On supported edge-aligned layouts, Explorer's native notification area and third-party tray icons remain available."
                    : "Hides the built-in taskbar on displays covered by Desktop Tuner. A companion recovery process restores it if Desktop Tuner exits unexpectedly; restart Windows Explorer or sign out if both processes are terminated.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("DesktopMutedTextBrush"),
                Margin = new Thickness(0, 0, 0, 16)
            });
            var startWithWindows = new CheckBox { Content = _shellOverlayMode ? "Start the shell overlay automatically when I sign in" : "Start the taskbar automatically when I sign in", IsChecked = _startWithWindows, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            startWithWindows.Checked += (_, _) => SetStartWithWindows(startWithWindows, true);
            startWithWindows.Unchecked += (_, _) => SetStartWithWindows(startWithWindows, false);
            PageContent.Children.Add(startWithWindows);
            var launchButton = new Button { Content = _shellOverlayMode ? "Show taskbar" : _replaceNativeTaskbar ? "Start replacement taskbar" : "Open Desktop Tuner taskbar overlay", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) };
            launchButton.Click += (_, _) => ShowTaskbar();
            PageContent.Children.Add(launchButton);
            if (_shellOverlayMode)
            {
                var exitShellOverlay = new Button
                {
                    Content = "Exit shell overlay and return to Explorer",
                    Style = (Style)FindResource("SecondaryButton"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                exitShellOverlay.Click += (_, _) => Application.Current.Shutdown();
                PageContent.Children.Add(exitShellOverlay);
            }
            else if (!_shellHostMode)
            {
                var startShellOverlay = new Button
                {
                    Content = "Start all-edition shell overlay",
                    Style = (Style)FindResource("SecondaryButton"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                startShellOverlay.Click += StartShellOverlay_Click;
                PageContent.Children.Add(startShellOverlay);
            }
            var overlayInfo = InfoCard(_shellOverlayMode ? "All-edition shell overlay" : _replaceNativeTaskbar ? "Experimental taskbar replacement" : "Live taskbar overlay", "Choose an edge, bar size and style, transparency or dynamic translucency, app button labels, icon size, spacing, and optional auto-hide. Aura highlights use each app icon's primary color; Dynamic Aura moves the highlight with the pointer. The custom taskbar lists open windows, activates or minimizes them, opens the companion Start menu on the same display, and opens the native Widgets board. Enable sign-in startup to keep the taskbar running in the background; right-click the bar to reopen Desktop Tuner settings or exit. In replacement mode, the built-in taskbar is hidden only on displays covered by Desktop Tuner and restored when its windows close; Quick Settings opens the native Wi-Fi, Bluetooth, brightness, and volume controls. Otherwise, the overlay can leave Windows' native notification area visible on supported bottom layouts. The shell overlay starts the custom desktop, Start, and taskbars at sign-in while Explorer remains available behind them.");
            PageContent.Children.Add(overlayInfo);
            RenderCustomShellControls();
            var info = InfoCard("Experimental Windows setting", "Microsoft may change or ignore these taskbar registry preferences in a future Windows release. The app stores the previous values so you can undo its last apply.");
            PageContent.Children.Add(info);
        }
        if (section == "Explorer")
        {
            var appearanceInfo = InfoCard("Appearance settings are shared with Windows", "App color mode affects File Explorer and other apps. System color mode also changes shell surfaces such as the taskbar and Start menu, and transparency effects apply system-wide. Some changes may need apps to restart or Windows to sign out and back in.");
            PageContent.Children.Add(appearanceInfo);
            var info = InfoCard("Classic context menu is experimental", "Windows 11 does not offer a supported switch for the classic full menu. This compatibility setting writes a per-user shell registration and may require File Explorer to restart or Windows to sign out.");
            PageContent.Children.Add(info);
            var explorerCompatibilityInfo = InfoCard("Legacy folder options depend on Windows", "Full-path title bars and separate folder processes use per-user Explorer preferences. Current tabbed File Explorer builds may ignore some legacy view settings; changes remain reversible from Profiles & restore.");
            PageContent.Children.Add(explorerCompatibilityInfo);
        }

        foreach (var setting in settings)
        {
            var row = Card();
            row.Margin = new Thickness(0, 0, 0, 11);
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            text.Children.Add(new TextBlock { Text = setting.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
            var settingDescription = ThemedText(setting.Description, "DesktopMutedTextBrush");
            settingDescription.FontSize = 12;
            settingDescription.TextWrapping = TextWrapping.Wrap;
            settingDescription.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(settingDescription);
            Grid.SetColumn(text, 0);
            layout.Children.Add(text);

            var combo = new ComboBox { ItemsSource = setting.Choices, DisplayMemberPath = nameof(SettingChoice.Label), SelectedValuePath = nameof(SettingChoice.Value), Height = 38, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4, 8, 4) };
            var selected = setting.Choices.FirstOrDefault(choice => choice.Value == _selectedValues[setting.Id]) ?? setting.Choices.First(choice => choice.Value == setting.DefaultValue);
            combo.SelectedValue = selected.Value;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedValue is not int value) return;
                _selectedValues[setting.Id] = value;
                if (value == _currentValues[setting.Id]) _dirty.Remove(setting.Id); else _dirty.Add(setting.Id);
                UpdateStatus();
            };
            _controls[setting.Id] = combo;
            Grid.SetColumn(combo, 1);
            layout.Children.Add(combo);
            row.Child = layout;
            PageContent.Children.Add(row);
        }
    }

    private void RenderProfiles()
    {
        AddPageHeading("Profiles & restore", "Save your chosen settings to a portable profile or load them back later.");
        var info = InfoCard("Last apply recovery", _settings.HasUndo
            ? "A recovery snapshot is available. Undo last apply restores the exact user-registry values recorded before the previous change."
            : "After your first apply, Desktop Tuner keeps a recovery snapshot of the original values. Nothing is changed until you apply a setting.");
        PageContent.Children.Add(info);

        var actions = Card();
        actions.Margin = new Thickness(0, 14, 0, 14);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Profile files", FontSize = 16, FontWeight = FontWeights.SemiBold });
        var profileDescription = ThemedText("Profiles are JSON files that contain only the app's known setting choices. Importing a file selects its values; it does not apply them automatically.", "DesktopMutedTextBrush");
        profileDescription.FontSize = 12;
        profileDescription.TextWrapping = TextWrapping.Wrap;
        profileDescription.Margin = new Thickness(0, 6, 0, 16);
        stack.Children.Add(profileDescription);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var export = new Button { Content = "Export current choices", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 10, 0) };
        export.Click += ExportProfile_Click;
        var import = new Button { Content = "Import profile", Style = (Style)FindResource("SecondaryButton") };
        import.Click += ImportProfile_Click;
        buttons.Children.Add(export);
        buttons.Children.Add(import);
        stack.Children.Add(buttons);
        actions.Child = stack;
        PageContent.Children.Add(actions);

        var restore = Card();
        var restoreStack = new StackPanel();
        restoreStack.Children.Add(new TextBlock { Text = "Restore your Windows settings", FontSize = 16, FontWeight = FontWeights.SemiBold });
        var restoreDescription = ThemedText("Undo last apply uses the snapshot from immediately before the last successful apply. It restores missing registry values to their original absent state as well.", "DesktopMutedTextBrush");
        restoreDescription.FontSize = 12;
        restoreDescription.TextWrapping = TextWrapping.Wrap;
        restoreDescription.Margin = new Thickness(0, 6, 0, 0);
        restoreStack.Children.Add(restoreDescription);
        restore.Child = restoreStack;
        PageContent.Children.Add(restore);
    }

    private void AddPageHeading(string title, string subtitle)
    {
        PageContent.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold });
        var subtitleText = ThemedText(subtitle, "DesktopMutedTextBrush");
        subtitleText.FontSize = 14;
        subtitleText.TextWrapping = TextWrapping.Wrap;
        subtitleText.Margin = new Thickness(0, 7, 0, 23);
        PageContent.Children.Add(subtitleText);
    }

    private Border InfoCard(string title, string description)
    {
        var stack = new StackPanel();
        var titleText = ThemedText(title, "DesktopAccentTextBrush");
        titleText.FontSize = 13;
        titleText.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(titleText);
        var descriptionText = ThemedText(description, "DesktopMutedTextBrush");
        descriptionText.FontSize = 12;
        descriptionText.TextWrapping = TextWrapping.Wrap;
        descriptionText.Margin = new Thickness(0, 5, 0, 0);
        stack.Children.Add(descriptionText);
        var card = Card();
        card.SetResourceReference(Border.BackgroundProperty, "DesktopAccentTintBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DesktopAccentTintBrush");
        card.Margin = new Thickness(0, 0, 0, 15);
        card.Child = stack;
        return card;
    }

    private static Border Card()
    {
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18) };
        card.SetResourceReference(Border.BackgroundProperty, "DesktopSurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DesktopBorderBrush");
        return card;
    }

    private static TextBlock ThemedText(string text, string brushKey)
    {
        var textBlock = new TextBlock { Text = text };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return textBlock;
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty.Count == 0)
        {
            SetStatus("Change at least one option, or import a profile, before applying.");
            return;
        }

        try
        {
            var desired = _dirty.ToDictionary(id => id, id => _selectedValues[id]);
            _settings.Apply(desired);
            foreach (var id in desired.Keys) _currentValues[id] = desired[id];
            DesktopTheme.Apply(_currentValues["explorer-app-mode"] == 0);
            _taskbarGrouping = (TaskbarGroupingMode)_currentValues["taskbar-combine"];
            _taskbarButtonAlignment = (TaskbarButtonAlignment)_currentValues["taskbar-alignment"];
            _dirty.Clear();
            UpdateTaskbarPreferences();
            SetStatus("Changes applied. Some Explorer and taskbar options may need Explorer to restart or Windows to sign out and back in.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows could not apply these settings. Any partial changes were rolled back.\n\n{ex.Message}", "Could not apply settings", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Apply failed. Check the error details and try again.");
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.UndoLastApply();
            foreach (var setting in SettingsCatalog.All)
            {
                var value = _settings.Read(setting);
                _currentValues[setting.Id] = value;
                _selectedValues[setting.Id] = value;
            }
            DesktopTheme.Apply(_currentValues["explorer-app-mode"] == 0);
            _taskbarGrouping = (TaskbarGroupingMode)_currentValues["taskbar-combine"];
            _taskbarButtonAlignment = (TaskbarButtonAlignment)_currentValues["taskbar-alignment"];
            _dirty.Clear();
            UpdateTaskbarPreferences();
            RenderPage(_activePage);
            SetStatus("Restored the values from before your last successful apply.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not restore settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Export Desktop Tuner profile", Filter = "Desktop Tuner profile (*.json)|*.json", FileName = "my-desktop-profile.json", DefaultExt = ".json", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _profileStore.SaveProfile(dialog.FileName, "My desktop profile", _selectedValues);
            SetStatus("Profile exported.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not export profile", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import Desktop Tuner profile", Filter = "Desktop Tuner profile (*.json)|*.json|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var profile = _profileStore.LoadProfile(dialog.FileName);
            foreach (var pair in profile.Settings)
            {
                var setting = SettingsCatalog.ById(pair.Key);
                if (setting.Choices.All(choice => choice.Value != pair.Value))
                    throw new InvalidDataException($"The profile contains an unsupported value for {setting.Name}.");
                _selectedValues[pair.Key] = pair.Value;
                if (pair.Value == _currentValues[pair.Key]) _dirty.Remove(pair.Key); else _dirty.Add(pair.Key);
            }
            RenderPage("Profiles");
            SetStatus($"Loaded profile: {profile.Name}. Review it, then choose Apply changes.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not import profile", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void UpdateStatus()
    {
        if (_dirty.Count == 0) SetStatus("Ready. Changes only apply when you choose Apply.");
        else SetStatus($"{_dirty.Count} setting{(_dirty.Count == 1 ? "" : "s")} changed and ready to apply.");
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        SystemBackdropService.TryApplyMica(this);
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource.AddHook(WindowMessageHook);
        var unavailableShortcuts = new List<string>();
        if (!RegisterHotKey(handle, StartMenuHotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_SPACE))
            unavailableShortcuts.Add("Ctrl+Alt+Space");
        if (!RegisterHotKey(handle, TaskbarAutoHideHotkeyId, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_T))
            unavailableShortcuts.Add("Win+Alt+T");
        if (unavailableShortcuts.Count > 0)
            SetStatus($"Global shortcut(s) {string.Join(" and ", unavailableShortcuts)} unavailable; use the app buttons or taskbar menu instead.");
        if ((_replaceWindowsKey || _replaceExplorerShortcut) && !ConfigureWindowsKeyHook())
        {
            _replaceWindowsKey = false;
            _replaceExplorerShortcut = false;
            if (!_shellHostMode && !_shellOverlayMode)
            {
                _replaceWindowsKeyPreference = false;
                SaveDesktopPreferences();
            }
            SetStatus("Shell shortcut integration could not start; the settings were turned off.");
        }
        if (ShellHostLaunchPolicy.ShouldStartTaskbar(_shellHostMode, _shellOverlayMode, _startInBackground && _startWithWindows))
        {
            Hide();
            Dispatcher.BeginInvoke(new Action(ShowTaskbar));
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if ((!_shellHostMode && !_shellOverlayMode) || Application.Current?.Dispatcher.HasShutdownStarted == true) return;
        e.Cancel = true;
        Hide();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _displayRefreshTimer.Stop();
        _displayRefreshTimer.Tick -= DisplayRefreshTimer_Tick;
        CloseTaskbars();
        _nativeTaskbarWatchTimer.Stop();
        _nativeTaskbarVisibility.Restore();
        var handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, StartMenuHotkeyId);
        UnregisterHotKey(handle, TaskbarAutoHideHotkeyId);
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowsKeyHook?.Dispose();
        _windowsKeyHook = null;
        _foregroundWindowHistory.Dispose();
    }

    private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded) return;
            var dark = _settings.Read(SettingsCatalog.ById("explorer-app-mode")) == 0;
            DesktopTheme.Apply(dark);
        }));
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WM_COPYDATA && TryReadOpenFolderCopyData(lParam, out var folderPath))
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OpenFolderFromShell(folderPath)));
            handled = true;
            return new IntPtr(1);
        }
        if (message == WM_COPYDATA && TryReadOpenShellLocationCopyData(lParam, out var shellLocation))
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OpenShellLocationFromShell(shellLocation)));
            handled = true;
            return new IntPtr(1);
        }
        if (message == WM_DWMCOLORIZATIONCOLORCHANGED)
            DesktopTheme.Apply(_currentValues["explorer-app-mode"] == 0);
        if (message == WM_APP_ACTIVATE_SETTINGS)
        {
            ShowSettingsWindow();
            handled = true;
            return IntPtr.Zero;
        }
        if (message == WM_DISPLAYCHANGE && _taskbarWindows.Any(window => window.IsVisible))
        {
            _displayRefreshTimer.Stop();
            _displayRefreshTimer.Start();
        }
        if (message == WM_HOTKEY && wParam.ToInt32() == StartMenuHotkeyId)
        {
            ShowStartMenu();
            handled = true;
        }
        else if (message == WM_HOTKEY && wParam.ToInt32() == TaskbarAutoHideHotkeyId)
        {
            ToggleTaskbarAutoHide();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleTaskbarAutoHide()
    {
        try
        {
            var preferences = TaskbarAutoHideHotkeyPolicy.Toggle(CreateDesktopPreferences());
            _preferences.Save(preferences);
            _taskbarAutoHide = preferences.AutoHide;
            ApplyTaskbarPreferences(preferences);
            SetStatus(_taskbarAutoHide ? "Taskbar auto-hide is on (Win+Alt+T)." : "Taskbar auto-hide is off (Win+Alt+T).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not toggle taskbar auto-hide", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static bool TryActivateExistingInstance(bool showSettings)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var window = FindWindow(null, "Desktop Tuner");
            if (window != IntPtr.Zero && PostMessage(window, WM_APP_ACTIVATE_SETTINGS, showSettings ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero)) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    public static bool TryOpenFolderInExistingInstance(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var payload = Marshal.StringToHGlobalUni(folderPath);
        try
        {
            var copyData = new CopyDataStruct
            {
                Data = new IntPtr(WM_COPYDATA_OPEN_FOLDER),
                ByteCount = checked((folderPath.Length + 1) * sizeof(char)),
                DataPointer = payload
            };
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var window = FindWindow(null, "Desktop Tuner");
                if (window == IntPtr.Zero)
                {
                    Thread.Sleep(50);
                    continue;
                }

                if (SendCopyData(window, WM_COPYDATA, IntPtr.Zero, ref copyData, SMTO_ABORTIFHUNG, 500, out var result) != IntPtr.Zero)
                {
                    if (result != IntPtr.Zero) return true;
                    Thread.Sleep(50);
                    continue;
                }

                return false;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(payload); }
    }

    public static bool TryOpenShellLocationInExistingInstance(string shellLocation)
    {
        if (!DesktopShellNamespaceCatalog.IsShellNamespaceLocation(shellLocation)) return false;
        var payload = Marshal.StringToHGlobalUni(shellLocation);
        try
        {
            var copyData = new CopyDataStruct
            {
                Data = new IntPtr(WM_COPYDATA_OPEN_SHELL_LOCATION),
                ByteCount = checked((shellLocation.Length + 1) * sizeof(char)),
                DataPointer = payload
            };
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var window = FindWindow(null, "Desktop Tuner");
                if (window == IntPtr.Zero)
                {
                    Thread.Sleep(50);
                    continue;
                }
                if (SendCopyData(window, WM_COPYDATA, IntPtr.Zero, ref copyData, SMTO_ABORTIFHUNG, 500, out var result) != IntPtr.Zero)
                {
                    if (result != IntPtr.Zero) return true;
                    Thread.Sleep(50);
                    continue;
                }
                return false;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(payload); }
    }

    private static bool TryReadOpenFolderCopyData(IntPtr dataPointer, out string folderPath)
    {
        folderPath = string.Empty;
        if (dataPointer == IntPtr.Zero) return false;
        var data = Marshal.PtrToStructure<CopyDataStruct>(dataPointer);
        if (data.Data.ToInt64() != WM_COPYDATA_OPEN_FOLDER || data.DataPointer == IntPtr.Zero || data.ByteCount is <= 0 or > 65536)
            return false;
        var value = Marshal.PtrToStringUni(data.DataPointer, data.ByteCount / sizeof(char));
        if (string.IsNullOrWhiteSpace(value)) return false;
        var terminator = value.IndexOf('\0');
        folderPath = terminator >= 0 ? value[..terminator] : value;
        return Path.IsPathFullyQualified(folderPath) && Directory.Exists(folderPath);
    }

    private static bool TryReadOpenShellLocationCopyData(IntPtr dataPointer, out string shellLocation)
    {
        shellLocation = string.Empty;
        if (dataPointer == IntPtr.Zero) return false;
        var data = Marshal.PtrToStructure<CopyDataStruct>(dataPointer);
        if (data.Data.ToInt64() != WM_COPYDATA_OPEN_SHELL_LOCATION || data.DataPointer == IntPtr.Zero || data.ByteCount is <= 0 or > 65536)
            return false;
        var value = Marshal.PtrToStringUni(data.DataPointer, data.ByteCount / sizeof(char));
        if (string.IsNullOrWhiteSpace(value)) return false;
        var terminator = value.IndexOf('\0');
        shellLocation = terminator >= 0 ? value[..terminator] : value;
        return DesktopShellNamespaceCatalog.IsShellNamespaceLocation(shellLocation);
    }

    public void OpenFolderFromShell(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            MessageBox.Show(this, "That folder is no longer available.", "Could not open folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenExplorer(Path.GetFullPath(folderPath));
    }

    private void OpenShellLocationFromShell(string shellLocation)
    {
        if (!DesktopShellNamespaceCatalog.IsShellNamespaceLocation(shellLocation)) return;
        if (DesktopShellNamespaceCatalog.IsCompanionExplorerLocation(shellLocation))
        {
            if (_explorerWindow is { IsVisible: true }) _explorerWindow.OpenShellLocationFromShell(shellLocation);
            else OpenExplorer(shellLocation);
            return;
        }
        OpenShellNamespaceBrowser(shellLocation);
    }

    private void ShowStartMenu() => ShowStartMenu(null);

    private void ShowStartMenu(TaskbarDisplay? display)
    {
        if (_startMenuWindow is { IsVisible: true })
        {
            _startMenuWindow.Close();
            return;
        }
        _startMenuWindow = new StartMenuWindow(_startMenuStyle, _pinnedStartApps, SavePinnedStartApps,
            startPlaces: _startMenuPlaces, recentAppCount: _startRecentAppCount, controlPanelApplets: _controlPanelApplets,
            openShellLocation: _shellHostMode ? TryOpenLocationInCompanionExplorer : null,
            openFileLocation: _shellHostMode ? TryOpenFileLocationInCompanionExplorer : null,
            pinTaskbarItem: TryPinTaskbarItem);
        _startMenuDisplay = display;
        _startMenuWindow.Closed += (_, _) => { _startMenuWindow = null; _startMenuDisplay = null; };
        if (display is not null)
            _startMenuWindow.SourceInitialized += (_, _) => PositionStartMenuWindow(display);
        _startMenuWindow.Show();
        if (display is null) PositionStartMenuWindow();
        _startMenuWindow.Activate();
    }

    private void PositionStartMenuWindow(TaskbarDisplay? display = null)
    {
        if (_startMenuWindow is null) return;
        display ??= _startMenuDisplay;
        if (display is not null)
        {
            var bounds = TaskbarLayoutCalculator.CalculateStartMenu(display, _startMenuWindow.Width, _startMenuWindow.Height,
                CreateDesktopPreferences(), _centerStartMenu);
            if (!TaskbarDisplayService.PositionWindow(_startMenuWindow, bounds))
                System.Diagnostics.Trace.TraceError($"Could not place Start menu on display {display.DeviceName}.");
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var taskbar = _taskbarWindows.FirstOrDefault(window => window.Display.IsPrimary && window.IsVisible)
            ?? _taskbarWindows.FirstOrDefault(window => window.IsVisible);
        var edge = taskbar is not null ? _taskbarEdge : TaskbarEdge.Bottom;
        switch (edge)
        {
            case TaskbarEdge.Top:
                _startMenuWindow.Left = workArea.Left + 12;
                _startMenuWindow.Top = workArea.Top + (taskbar?.Height ?? 54) + 12;
                break;
            case TaskbarEdge.Left:
                _startMenuWindow.Left = workArea.Left + (taskbar?.Width ?? 176) + 12;
                _startMenuWindow.Top = workArea.Top + 12;
                break;
            case TaskbarEdge.Right:
                _startMenuWindow.Left = workArea.Right - _startMenuWindow.Width - (taskbar?.Width ?? 176) - 12;
                _startMenuWindow.Top = workArea.Top + 12;
                break;
            default:
                _startMenuWindow.Left = workArea.Left + 12;
                _startMenuWindow.Top = Math.Max(workArea.Top + 12, workArea.Bottom - _startMenuWindow.Height - 12);
                break;
        }
        if (_centerStartMenu)
        {
            if (edge is TaskbarEdge.Top or TaskbarEdge.Bottom)
                _startMenuWindow.Left = workArea.Left + (workArea.Width - _startMenuWindow.Width) / 2;
            else
                _startMenuWindow.Top = workArea.Top + (workArea.Height - _startMenuWindow.Height) / 2;
        }
    }

    private void ShowTaskbar()
    {
        if (_taskbarWindows.FirstOrDefault(window => window.IsVisible) is { } existing)
        {
            existing.Activate();
            return;
        }
        try
        {
            var preferences = CreateTaskbarRuntimePreferences();
            if (_shellHostMode) _shellHostTaskbarNativeTrayIntegrated = _shellHostNativeTrayIntegrated;
            var showAllDisplays = ShellHostLaunchPolicy.ShouldCoverAllDisplays(_shellHostMode, _taskbarOnAllDisplays, _shellOverlayMode);
            var hideNativeTaskbar = ShellHostLaunchPolicy.ShouldHideNativeTaskbar(_shellHostMode, _shellOverlayMode, _replaceNativeTaskbar);
            foreach (var display in TaskbarDisplayService.Select(showAllDisplays))
                AddTaskbarWindow(display, preferences);
            if (hideNativeTaskbar)
            {
                var displays = TaskbarDisplayService.Select(showAllDisplays);
                using var watchdog = NativeTaskbarWatchdog.Start(Environment.ProcessId, _nativeTaskbarVisibility.SnapshotPath);
                if (!_nativeTaskbarVisibility.HideForDisplays(displays))
                {
                    throw new InvalidOperationException("Windows did not expose a taskbar on the selected displays, so replacement mode could not start.");
                }
            }
            foreach (var taskbar in _taskbarWindows)
            {
                var reserveWorkArea = hideNativeTaskbar || ShouldReserveShellHostWorkArea(taskbar, preferences);
                if (!reserveWorkArea) continue;
                if (!taskbar.EnableReplacementWorkArea(true))
                {
                    if (hideNativeTaskbar)
                        throw new InvalidOperationException($"Windows could not reserve a work area for the replacement taskbar on {taskbar.Display.DeviceName}. Desktop Tuner will restore the Windows taskbar.");
                    System.Diagnostics.Trace.TraceWarning($"Windows could not reserve a work area for the shell taskbar on {taskbar.Display.DeviceName}; it will remain an overlay.");
                }
            }
            if (_shellHostMode) _shellHostTrayRefreshTimer.Start();
            if (hideNativeTaskbar) _nativeTaskbarWatchTimer.Start();
            SetStatus(_shellOverlayMode
                ? "Desktop Tuner shell overlay is running on every display. Explorer's notification area remains exposed where the selected layout supports it."
                : _shellHostMode
                ? "Desktop Tuner shell host is running. Its taskbars and Start menu are active on every display."
                    : showAllDisplays
                        ? hideNativeTaskbar
                            ? "Desktop Tuner taskbar replacement is running on all displays. Close it to restore Windows taskbars."
                            : "Desktop Tuner taskbar overlays are running on all displays. Close one to reveal the Windows taskbar everywhere."
                        : hideNativeTaskbar
                            ? "Desktop Tuner taskbar replacement is running on the primary display. Close it to restore the Windows taskbar."
                            : "Desktop Tuner taskbar overlay is running on the primary display. Close it to reveal the Windows taskbar.");
        }
        catch (Exception ex)
        {
            CloseTaskbars();
            if (_replaceNativeTaskbar)
            {
                _replaceNativeTaskbar = false;
                try { _preferences.Save(CreateDesktopPreferences()); }
                catch (Exception saveException) { System.Diagnostics.Trace.TraceError($"Could not disable taskbar replacement after startup failed: {saveException}"); }
            }
            MessageBox.Show(this, ex.Message, "Could not start the custom taskbar", MessageBoxButton.OK, MessageBoxImage.Error);
            if (_shellOverlayMode) Application.Current?.Shutdown();
        }
    }

    private TaskbarWindow AddTaskbarWindow(TaskbarDisplay display, DesktopPreferences preferences)
    {
        var taskbar = new TaskbarWindow(display, targetDisplay => ShowStartMenu(targetDisplay), () => _startMenuWindow?.IsVisible == true, preferences, _taskbarWindowOrder, SaveDesktopPreferences, CloseTaskbars, ShowSettingsWindow, QuitApplication,
            showDesktop: _shellHostMode ? ToggleShowDesktop : null,
            focusSystemArea: _shellHostMode ? FocusTaskbarSystemArea : null,
            executePowerUserCommand: _shellHostMode ? ExecuteShellHostPowerUserCommand : null,
            openDirectoryInCompanionExplorer: _shellHostMode ? path => OpenExplorer(path) : null,
            openFileLocationInCompanionExplorer: _shellHostMode ? OpenPinnedFileLocationInCompanionExplorer : null,
            openShellLocationInCompanionExplorer: _shellHostMode ? TryOpenLocationInCompanionExplorer : null,
            pinStartItem: TryPinStartItem,
            shellHostMode: _shellHostMode);
        taskbar.ContentRendered += TaskbarWindow_ContentRendered;
        taskbar.Closed += (_, _) =>
        {
            taskbar.ContentRendered -= TaskbarWindow_ContentRendered;
            _taskbarWindows.Remove(taskbar);
            if (!_closingTaskbars && !_reconcilingDisplayTopology) CloseTaskbars();
        };
        _taskbarWindows.Add(taskbar);
        taskbar.Show();
        return taskbar;
    }

    private void OpenPinnedFileLocationInCompanionExplorer(string path)
    {
        if (Directory.Exists(path))
        {
            OpenExplorer(path);
            return;
        }

        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder) && File.Exists(path) && Directory.Exists(folder))
            OpenExplorer(folder, path);
    }

    private void TaskbarWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (!_shellHostMode || _shellHostReadySignaled) return;
        _shellHostReadySignaled = true;
        _shellHostHeartbeatTimer.Tick += ShellHostHeartbeatTimer_Tick;
        _shellHostHeartbeatTimer.Start();
        CustomShellPolicy.SignalHostHeartbeat();
        CustomShellPolicy.SignalHostReady();
    }

    private void ShellHostHeartbeatTimer_Tick(object? sender, EventArgs e) => CustomShellPolicy.SignalHostHeartbeat();

    private void DisplayRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _displayRefreshTimer.Stop();
        if (!_taskbarWindows.Any(window => window.IsVisible)) return;

        try
        {
            var desiredDisplays = TaskbarDisplayService.Select(ShellHostLaunchPolicy.ShouldCoverAllDisplays(_shellHostMode, _taskbarOnAllDisplays, _shellOverlayMode));
            var topology = TaskbarDisplayService.PlanTopologyChange(_taskbarWindows.Select(window => window.Display), desiredDisplays);
            var preferences = CreateTaskbarRuntimePreferences();
            _reconcilingDisplayTopology = true;
            try
            {
                foreach (var deviceName in topology.RemovedDeviceNames)
                {
                    var removed = _taskbarWindows.FirstOrDefault(window => string.Equals(window.Display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
                    if (removed is null) continue;
                    if (removed.IsVisible) removed.Close();
                    _taskbarWindows.Remove(removed);
                }
            }
            finally { _reconcilingDisplayTopology = false; }

            foreach (var display in topology.Retained)
            {
                var taskbar = _taskbarWindows.FirstOrDefault(window => string.Equals(window.Display.DeviceName, display.DeviceName, StringComparison.OrdinalIgnoreCase));
                taskbar?.UpdateDisplay(display);
            }
            foreach (var display in topology.Added)
            {
                var taskbar = AddTaskbarWindow(display, preferences);
                if (ShouldReserveShellHostWorkArea(taskbar, preferences)
                    && !taskbar.EnableReplacementWorkArea(true))
                    System.Diagnostics.Trace.TraceWarning($"Windows could not reserve a work area for the shell taskbar on {display.DeviceName}; it will remain an overlay.");
            }

            if (ShellHostLaunchPolicy.ShouldHideNativeTaskbar(_shellHostMode, _shellOverlayMode, _replaceNativeTaskbar))
                MaintainNativeTaskbars();
            RepositionOpenStartMenu();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Could not reconcile taskbars after a display change: {ex}");
            SetStatus("Display layout changed, but the taskbar layout could not be fully refreshed. Retry by toggling taskbar display coverage in Settings.");
        }
    }

    private void ShellHostTrayRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!_shellHostMode || !_taskbarWindows.Any(window => window.IsVisible))
        {
            _shellHostTrayRefreshTimer.Stop();
            return;
        }

        try
        {
            var preferences = CreateTaskbarRuntimePreferences();
            if (!ShellHostLaunchPolicy.ShouldReconcileNativeTrayIntegration(
                    _shellHostMode, _shellHostTaskbarNativeTrayIntegrated, _shellHostNativeTrayIntegrated)) return;

            ApplyTaskbarPreferences(preferences);
            System.Diagnostics.Trace.TraceInformation(_shellHostTaskbarNativeTrayIntegrated
                ? "An Explorer notification area appeared; shell-host taskbars now expose it and release their AppBar work-area reservations."
                : "The Explorer notification area disappeared; shell-host taskbars restored custom system controls and AppBar work-area reservations.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Could not reconcile shell-host notification-area integration: {ex}");
        }
    }

    private void RepositionOpenStartMenu()
    {
        if (_startMenuWindow?.IsVisible != true) return;
        var taskbar = _taskbarWindows.FirstOrDefault(window => window.IsVisible && _startMenuDisplay is not null
                && string.Equals(window.Display.DeviceName, _startMenuDisplay.DeviceName, StringComparison.OrdinalIgnoreCase))
            ?? _taskbarWindows.FirstOrDefault(window => window.Display.IsPrimary && window.IsVisible)
            ?? _taskbarWindows.FirstOrDefault(window => window.IsVisible);
        _startMenuDisplay = taskbar?.Display;
        PositionStartMenuWindow(taskbar?.Display);
    }

    private void CloseTaskbars()
    {
        if (_closingTaskbars) return;
        _closingTaskbars = true;
        try
        {
            _displayRefreshTimer.Stop();
            _shellHostTrayRefreshTimer.Stop();
            _nativeTaskbarWatchTimer.Stop();
            foreach (var taskbar in _taskbarWindows.ToArray())
                if (taskbar.IsVisible) taskbar.Close();
            _taskbarWindows.Clear();
            _nativeTaskbarVisibility.Restore();
        }
        finally { _closingTaskbars = false; }
    }

    private void MaintainNativeTaskbars()
    {
        if (!ShellHostLaunchPolicy.ShouldHideNativeTaskbar(_shellHostMode, _shellOverlayMode, _replaceNativeTaskbar) || !_taskbarWindows.Any(window => window.IsVisible))
        {
            _nativeTaskbarWatchTimer.Stop();
            _nativeTaskbarVisibility.Restore();
            return;
        }

        try
        {
            if (!_nativeTaskbarVisibility.HideForDisplays(TaskbarDisplayService.Select(ShellHostLaunchPolicy.ShouldCoverAllDisplays(_shellHostMode, _taskbarOnAllDisplays, _shellOverlayMode))))
                throw new InvalidOperationException("Windows did not expose a taskbar on the selected displays.");
            foreach (var taskbar in _taskbarWindows)
                if (!taskbar.EnableReplacementWorkArea(true))
                    throw new InvalidOperationException($"Windows could not reserve a work area for the replacement taskbar on {taskbar.Display.DeviceName}.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Could not keep native taskbars hidden during replacement mode: {ex}");
            _replaceNativeTaskbar = false;
            try { _preferences.Save(CreateDesktopPreferences()); }
            catch (Exception saveException) { System.Diagnostics.Trace.TraceError($"Could not disable taskbar replacement after work-area registration failed: {saveException}"); }
            CloseTaskbars();
            SetStatus("Windows taskbar restored because Desktop Tuner could not reserve the replacement work area.");
            if (_shellOverlayMode) Application.Current?.Shutdown();
        }
    }

    private DesktopPreferences CreateDesktopPreferences() => new(_taskbarEdge, _taskbarSize, _taskbarAutoHide, _pinnedApps.ToList(), _replaceWindowsKeyPreference, _startMenuStyle, _taskbarOnAllDisplays, _taskbarLayout, _taskbarGrouping, _taskbarButtonAlignment, _taskbarLabelVisibility != TaskbarLabelVisibility.Never, _taskbarIconSize, _taskbarButtonSpacing, _startWithWindows, _taskbarAutoHideWhenMaximized, _taskbarTransparency, _pinnedStartApps.ToList(), _replaceNativeTaskbar, _taskbarDynamicTransparency, _taskbarButtonEffect, _startMenuPlaces, _startRecentAppCount, _taskbarSystemButtons, _centerStartMenu, _taskbarWindowDisplayMode, _folderShellIntegrationEnabled, _taskbarShowWindowsFromAllVirtualDesktops, _replaceExplorerShortcut, _taskbarVisualStyle, _controlPanelApplets, _taskbarWeather, _taskbarLocked, _taskbarLabelVisibility);

    private DesktopPreferences CreateTaskbarRuntimePreferences(DesktopPreferences? preferences = null)
    {
        preferences ??= CreateDesktopPreferences();
        var nativeTrayAvailable = _shellHostMode && TaskbarDisplayService.Select(allDisplays: true)
            .Any(display => NativeTaskbarTrayService.FindTrayBounds(display) is not null);
        _shellHostNativeTrayIntegrated = ShellHostLaunchPolicy.ShouldUseNativeTrayIntegration(
            _shellHostMode, _shellOverlayMode, preferences.ReplaceNativeTaskbar, nativeTrayAvailable);
        return preferences with
        {
            ReplaceNativeTaskbar = !_shellHostNativeTrayIntegrated
        };
    }

    private void ApplyTaskbarPreferences(DesktopPreferences preferences)
    {
        var runtimePreferences = _shellHostMode ? CreateTaskbarRuntimePreferences(preferences) : preferences;
        foreach (var taskbar in _taskbarWindows.ToArray()) taskbar.SetPreferences(runtimePreferences);
        if (!_shellHostMode) return;

        _shellHostTaskbarNativeTrayIntegrated = _shellHostNativeTrayIntegrated;
        foreach (var taskbar in _taskbarWindows.ToArray())
        {
            var reserveWorkArea = ShouldReserveShellHostWorkArea(taskbar, runtimePreferences);
            if (!taskbar.EnableReplacementWorkArea(reserveWorkArea) && reserveWorkArea)
                System.Diagnostics.Trace.TraceWarning($"Windows could not reserve a work area for the shell taskbar on {taskbar.Display.DeviceName}; it will remain an overlay.");
        }
    }

    private bool ShouldReserveShellHostWorkArea(TaskbarWindow taskbar, DesktopPreferences runtimePreferences)
    {
        if (!_shellHostMode) return false;
        var trayBounds = NativeTaskbarTrayService.FindTrayBounds(taskbar.Display);
        return TaskbarTrayIntegrationPolicy.ShouldReserveShellHostWorkArea(taskbar.Display, runtimePreferences, trayBounds);
    }

    private void SetStartMenuCentered(bool centered)
    {
        _centerStartMenu = centered;
        SaveDesktopPreferences();
        PositionStartMenuWindow();
    }

    private void SetTaskbarSystemButton(TaskbarSystemButton button, bool isVisible)
    {
        _taskbarSystemButtons = _taskbarSystemButtons.WithVisibility(button, isVisible);
        SaveDesktopPreferences();
    }

    private void SetTaskbarClockSeconds(CheckBox checkBox, bool enabled)
    {
        try
        {
            TaskbarClockPolicy.SetShowSeconds(enabled);
            SetStatus(enabled ? "Taskbar clock seconds are enabled." : "Taskbar clock seconds are disabled.");
        }
        catch (Exception ex)
        {
            _updatingTaskbarClock = true;
            checkBox.IsChecked = !enabled;
            _updatingTaskbarClock = false;
            MessageBox.Show(this, ex.Message, "Could not change taskbar clock settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SavePlaceVisibility(string placeId, bool isVisible, Action refresh)
    {
        var visible = _startMenuPlaces.Visible!.ToList();
        if (isVisible && !visible.Contains(placeId, StringComparer.OrdinalIgnoreCase)) visible.Add(placeId);
        else if (!isVisible) visible.RemoveAll(id => string.Equals(id, placeId, StringComparison.OrdinalIgnoreCase));
        SaveStartMenuPlaces(StartMenuPlaceCatalog.Normalize(_startMenuPlaces with { Visible = visible }));
        refresh();
    }

    private void MoveStartPlace(string placeId, int offset, Action refresh)
    {
        SaveStartMenuPlaces(StartMenuPlaceCatalog.Move(_startMenuPlaces, placeId, offset));
        refresh();
    }

    private bool SaveStartMenuPlaces(StartMenuPlacePreferences preferences)
    {
        var normalized = StartMenuPlaceCatalog.Normalize(preferences);
        try
        {
            _preferences.Save(CreateDesktopPreferences() with { StartMenuPlaces = normalized });
            _startMenuPlaces = normalized;
            _startMenuWindow?.SetStartPlaces(normalized);
            SetStatus("Start menu places saved.");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save Start menu places", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void SaveControlPanelAppletVisibility(string appletId, bool isVisible, Action refresh)
    {
        var visible = _controlPanelApplets.Visible!.ToList();
        if (isVisible && !visible.Contains(appletId, StringComparer.OrdinalIgnoreCase)) visible.Add(appletId);
        else if (!isVisible) visible.RemoveAll(id => string.Equals(id, appletId, StringComparison.OrdinalIgnoreCase));
        SaveControlPanelApplets(ControlPanelAppletCatalog.Normalize(_controlPanelApplets with { Visible = visible }), refresh);
    }

    private void MoveControlPanelApplet(string appletId, int offset, Action refresh) =>
        SaveControlPanelApplets(ControlPanelAppletCatalog.Move(_controlPanelApplets, appletId, offset), refresh);

    private bool SaveControlPanelApplets(ControlPanelAppletPreferences preferences, Action? refresh = null)
    {
        var normalized = ControlPanelAppletCatalog.Normalize(preferences);
        try
        {
            _preferences.Save(CreateDesktopPreferences() with { ControlPanelApplets = normalized });
            _controlPanelApplets = normalized;
            _startMenuWindow?.SetControlPanelApplets(normalized);
            SetStatus("Control Panel applet shortcuts saved.");
            refresh?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save Control Panel applets", MessageBoxButton.OK, MessageBoxImage.Error);
            refresh?.Invoke();
            return false;
        }
    }

    private bool SavePinnedStartApps(IReadOnlyList<AppEntry> apps)
    {
        var pins = StartPinCatalog.Normalize(apps).ToList();
        try
        {
            _preferences.Save(CreateDesktopPreferences() with { PinnedStartApps = pins });
            _pinnedStartApps = pins;
            SetStatus("Start pins saved.");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save Start pins", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void UpdateTaskbarPreferences()
    {
        var preferences = CreateDesktopPreferences();
        ApplyTaskbarPreferences(preferences);
    }

    private void SaveDesktopPreferences()
    {
        try
        {
            SaveDesktopPreferences(CreateDesktopPreferences());
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save taskbar preferences", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SaveDesktopPreferences(DesktopPreferences preferences)
    {
        try
        {
            var displayModeChanged = _taskbarOnAllDisplays != preferences.TaskbarOnAllDisplays;
            var replacementModeChanged = _replaceNativeTaskbar != preferences.ReplaceNativeTaskbar;
            _preferences.Save(preferences);
            _taskbarEdge = preferences.TaskbarEdge;
            _taskbarSize = preferences.TaskbarSize;
            _taskbarAutoHide = preferences.AutoHide;
            _taskbarAutoHideWhenMaximized = preferences.AutoHideWhenMaximized;
            _taskbarTransparency = preferences.TaskbarTransparency;
            _taskbarDynamicTransparency = preferences.TaskbarDynamicTransparency;
            _taskbarLocked = preferences.TaskbarLocked;
            _pinnedApps = preferences.PinnedApps ?? [];
            _pinnedStartApps = StartPinCatalog.Normalize(preferences.PinnedStartApps).ToList();
            _startMenuPlaces = StartMenuPlaceCatalog.Normalize(preferences.StartMenuPlaces);
            _controlPanelApplets = ControlPanelAppletCatalog.Normalize(preferences.ControlPanelApplets);
            _replaceWindowsKeyPreference = preferences.ReplaceWindowsKey;
            _replaceWindowsKey = _shellHostMode || _shellOverlayMode || preferences.ReplaceWindowsKey;
            _replaceExplorerShortcut = ShellHostLaunchPolicy.ShouldReplaceExplorerShortcut(_shellHostMode, preferences.ReplaceExplorerShortcut);
            _startMenuStyle = preferences.StartMenuStyle;
            _startRecentAppCount = preferences.StartRecentAppCount;
            _centerStartMenu = preferences.CenterStartMenu;
            _taskbarOnAllDisplays = preferences.TaskbarOnAllDisplays;
            _taskbarWindowDisplayMode = preferences.TaskbarWindowDisplayMode;
            _taskbarShowWindowsFromAllVirtualDesktops = preferences.TaskbarShowWindowsFromAllVirtualDesktops;
            _taskbarLayout = preferences.TaskbarLayout;
            _taskbarVisualStyle = preferences.TaskbarVisualStyle;
            _taskbarGrouping = preferences.TaskbarGrouping;
            _taskbarButtonAlignment = preferences.TaskbarButtonAlignment;
            _taskbarShowLabels = preferences.TaskbarShowLabels;
            _taskbarLabelVisibility = !preferences.TaskbarShowLabels && preferences.TaskbarLabelVisibility == TaskbarLabelVisibility.Always
                ? TaskbarLabelVisibility.Never : preferences.TaskbarLabelVisibility;
            _taskbarIconSize = preferences.TaskbarIconSize;
            _taskbarButtonSpacing = preferences.TaskbarButtonSpacing;
            _taskbarButtonEffect = preferences.TaskbarButtonEffect;
            _taskbarSystemButtons = TaskbarSystemButtonVisibility.Normalize(preferences.TaskbarSystemButtons);
            _taskbarWeather = TaskbarWeatherPolicy.Normalize(preferences.TaskbarWeather);
            _startWithWindows = preferences.StartWithWindows;
            _replaceNativeTaskbar = preferences.ReplaceNativeTaskbar;
            if ((displayModeChanged || replacementModeChanged) && _taskbarWindows.Any(window => window.IsVisible))
            {
                CloseTaskbars();
                ShowTaskbar();
            }
            else
            {
                ApplyTaskbarPreferences(preferences);
            }
            _startMenuWindow?.SetStyle(_startMenuStyle);
            _startMenuWindow?.SetRecentAppCount(_startRecentAppCount);
            _startMenuWindow?.SetStartPlaces(_startMenuPlaces);
            _startMenuWindow?.SetControlPanelApplets(_controlPanelApplets);
            SetStatus("Desktop preferences saved.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save taskbar preferences", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SetStartWithWindows(CheckBox checkBox, bool enabled)
    {
        if (_startWithWindows == enabled) return;
        try
        {
            StartupShortcutService.SetEnabled(enabled, _shellOverlayMode);
            _preferences.Save(CreateDesktopPreferences() with { StartWithWindows = enabled });
            _startWithWindows = enabled;
            SetStatus(enabled
                ? _shellOverlayMode ? "The Desktop Tuner shell overlay will start at sign-in." : "The taskbar will start automatically at sign-in."
                : "Automatic Desktop Tuner startup is disabled.");
        }
        catch (Exception ex)
        {
            try { StartupShortcutService.SetEnabled(_startWithWindows, _shellOverlayMode); }
            catch (Exception rollbackError) { ex = new AggregateException("The Startup shortcut could not be restored after saving failed.", ex, rollbackError); }
            checkBox.IsChecked = _startWithWindows;
            MessageBox.Show(this, ex.Message, "Could not change sign-in startup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetFolderShellIntegration(CheckBox checkBox, bool enabled)
    {
        if (_folderShellIntegrationEnabled == enabled) return;
        try
        {
            FolderShellIntegrationService.SetEnabled(enabled);
            _preferences.Save(CreateDesktopPreferences() with { FolderShellIntegrationEnabled = enabled });
            _folderShellIntegrationEnabled = enabled;
            SetStatus(enabled
                ? "Folder context menus can now open locations in Desktop Tuner Explorer."
                : "Desktop Tuner folder context menu commands were removed.");
        }
        catch (Exception ex)
        {
            try { FolderShellIntegrationService.SetEnabled(_folderShellIntegrationEnabled); }
            catch (Exception rollbackError) { ex = new AggregateException("The folder context menu could not be restored after saving failed.", ex, rollbackError); }
            checkBox.IsChecked = _folderShellIntegrationEnabled;
            MessageBox.Show(this, ex.Message, "Could not change folder context menus", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowSettingsWindow()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        RenderPage("Taskbar");
        Activate();
    }

    private void RenderCustomShellControls()
    {
        var executablePath = Environment.ProcessPath;
        var shellCommand = CustomShellPolicy.ReadCurrentUserShellCommand();
        var isConfigured = CustomShellPolicy.TargetsExecutable(shellCommand, executablePath);
        var supportedEdition = CustomShellPolicy.IsSupportedWindowsEdition();
        var hasOtherShell = !string.IsNullOrWhiteSpace(shellCommand) && !isConfigured;
        var status = !supportedEdition
            ? "The per-user alternate-shell policy is supported on Windows Pro, Enterprise, Education, and IoT Enterprise editions."
            : isConfigured
                ? "Desktop Tuner is configured as this user's shell. The change takes effect at sign-in."
                : hasOtherShell
                    ? "Another custom shell is configured. Desktop Tuner will leave that setting unchanged."
                    : "Windows Explorer remains the sign-in shell until you opt in here.";
        PageContent.Children.Add(InfoCard("Desktop Tuner sign-in shell", status + " This replaces Explorer for this user at the next sign-in. A supervisor retries once after a crash, then starts Explorer and clears Desktop Tuner's per-user shell setting if both attempts fail. A normal exit also returns to Explorer. Keep Ctrl+Alt+Delete recovery available and test on a separate account before using it every day."));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        var configure = new Button
        {
            Content = "Use Desktop Tuner at sign-in",
            Style = (Style)FindResource(isConfigured ? "SecondaryButton" : "PrimaryButton"),
            IsEnabled = supportedEdition && executablePath is not null && !hasOtherShell && !isConfigured && !ShellLauncherService.HasOwnedConfiguration,
            Margin = new Thickness(0, 0, 10, 0)
        };
        configure.Click += ConfigureCustomShell_Click;
        actions.Children.Add(configure);

        var restore = new Button
        {
            Content = "Restore Windows Explorer",
            Style = (Style)FindResource("SecondaryButton"),
            IsEnabled = isConfigured,
            Margin = new Thickness(0, 0, 10, 0)
        };
        restore.Click += RestoreCustomShell_Click;
        actions.Children.Add(restore);
        if (_shellHostMode)
        {
            var exitShellHost = new Button
            {
                Content = "Exit shell replacement and start Explorer",
                Style = (Style)FindResource("SecondaryButton")
            };
            exitShellHost.Click += ExitShellHost_Click;
            actions.Children.Add(exitShellHost);
        }
        PageContent.Children.Add(actions);

        if (ShellLauncherService.IsSupportedWindowsEdition())
        {
            var shellLauncherConfigured = ShellLauncherService.HasOwnedConfiguration;
            AddPageHeading("Windows Shell Launcher", "For licensed Enterprise, Education, and IoT Enterprise devices with the optional Shell Launcher feature enabled.");
            PageContent.Children.Add(InfoCard("Per-user Shell Launcher", shellLauncherConfigured
                ? "Desktop Tuner saved the previous default and enablement state while assigning its supervised shell to this account. Other accounts use Explorer."
                : "This configures only the current account, keeps Explorer as the default for other accounts, and saves the prior default so Desktop Tuner can restore it. Existing or enabled Shell Launcher configurations are left unchanged. The device must be licensed for Shell Launcher, and its optional Windows feature must already be enabled."));

            var shellLauncherActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            var configureShellLauncher = new Button
            {
                Content = "Configure Shell Launcher for this user",
                Style = (Style)FindResource("PrimaryButton"),
                IsEnabled = !shellLauncherConfigured,
                Margin = new Thickness(0, 0, 10, 0)
            };
            configureShellLauncher.Click += ConfigureShellLauncher_Click;
            shellLauncherActions.Children.Add(configureShellLauncher);

            var restoreShellLauncher = new Button
            {
                Content = "Restore Shell Launcher",
                Style = (Style)FindResource("SecondaryButton"),
                IsEnabled = shellLauncherConfigured
            };
            restoreShellLauncher.Click += RestoreShellLauncher_Click;
            shellLauncherActions.Children.Add(restoreShellLauncher);
            PageContent.Children.Add(shellLauncherActions);
        }
    }

    private void StartShellOverlay_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Start Desktop Tuner's all-edition shell overlay? It replaces the visible desktop, Start menu, and taskbar for this session while Windows Explorer continues running behind it. Exit the overlay from Taskbar settings or the taskbar menu to return to Explorer.",
            "Start shell overlay", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Windows could not determine the Desktop Tuner process path.");
            var assemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("Windows could not determine the Desktop Tuner application path.");
            var startInfo = StartupShortcutService.BuildProcessStartInfo(processPath, assemblyPath, shellOverlayMode: true);
            if (_startWithWindows) StartupShortcutService.SetEnabled(true, shellOverlayMode: true);
            Application.Current.Exit += (_, _) =>
            {
                try
                {
                    _ = Process.Start(startInfo)
                        ?? throw new InvalidOperationException("Windows did not start the shell overlay process.");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    MessageBox.Show($"Could not start the shell overlay. Windows Explorer remains available.{Environment.NewLine}{ex.Message}",
                        "Could not start shell overlay", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Could not start shell overlay", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ConfigureShellLauncher_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Desktop Tuner will configure Shell Launcher for this account and enable Shell Launcher for the device. Other accounts will keep Explorer as their default. The current Shell Launcher configuration is changed only when no existing mappings are present, and Desktop Tuner saves the previous default for restore. Administrator approval is required. Use a non-administrator test account and keep a separate administrator account on Explorer. Continue?",
            "Configure Windows Shell Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        var result = await ShellLauncherService.ConfigureCurrentUserAsync();
        if (result.Status == ShellLauncherOperationStatus.Success)
        {
            SetStatus(result.Message);
            RenderPage("Taskbar");
            return;
        }
        MessageBox.Show(this, result.Message, "Shell Launcher was not configured", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void RestoreShellLauncher_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Remove Desktop Tuner's Shell Launcher mapping for this account and restore the saved default and enablement state when no other Shell Launcher mappings have been added? Administrator approval is required.",
            "Restore Windows Shell Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var result = await ShellLauncherService.RestoreCurrentUserAsync();
        if (result.Status == ShellLauncherOperationStatus.Success)
        {
            SetStatus(result.Message);
            RenderPage("Taskbar");
            return;
        }
        MessageBox.Show(this, result.Message, "Shell Launcher restore needs review", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ConfigureCustomShell_Click(object sender, RoutedEventArgs e)
    {
        if (ShellLauncherService.HasOwnedConfiguration)
        {
            MessageBox.Show(this, "Restore Desktop Tuner's Shell Launcher mapping before configuring the separate per-user alternate-shell policy.", "Another shell mode is active", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (Environment.ProcessPath is not { } executablePath) return;
        var answer = MessageBox.Show(this,
            "Desktop Tuner will replace Explorer as this user's shell at the next sign-in. If it fails to start, use Ctrl+Alt+Delete, open Task Manager, and run explorer.exe; then return to Taskbar settings to restore Windows Explorer. Continue?",
            "Use Desktop Tuner as the sign-in shell", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            CustomShellPolicy.ConfigureForExecutable(executablePath);
            SetStatus("Desktop Tuner will start as this user's shell at the next sign-in.");
            RenderPage("Taskbar");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Security.SecurityException)
        {
            MessageBox.Show(this, ex.Message, "Could not configure Desktop Tuner as the shell", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreCustomShell_Click(object sender, RoutedEventArgs e)
    {
        if (Environment.ProcessPath is not { } executablePath) return;
        var answer = MessageBox.Show(this,
            "Remove Desktop Tuner's per-user shell setting? Windows Explorer will start as this user's shell after the next sign-in.",
            "Restore Windows Explorer", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (!CustomShellPolicy.RestoreDefaultShell(executablePath))
            {
                MessageBox.Show(this, "The current shell setting no longer points to Desktop Tuner, so it was left unchanged.", "Shell setting unchanged", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SetStatus("Windows Explorer will start as this user's shell at the next sign-in.");
            RenderPage("Taskbar");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            MessageBox.Show(this, ex.Message, "Could not restore Windows Explorer as the shell", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExitShellHost_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Exit Desktop Tuner's shell replacement and start Windows Explorer for this session?",
            "Exit shell replacement", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            Application.Current.Shutdown();
    }

    private void OpenExplorer(string? initialPath = null, string? selectPath = null)
    {
        if (_explorerWindow is { IsVisible: true })
        {
            if (!string.IsNullOrWhiteSpace(selectPath)) _explorerWindow.OpenFileLocationFromShell(selectPath);
            else if (!string.IsNullOrWhiteSpace(initialPath)) _explorerWindow.OpenFolderFromShell(initialPath);
            _explorerWindow.Activate();
            return;
        }

        _explorerWindow = new ExplorerWindow(
            initialPath: initialPath,
            showHiddenItems: _currentValues["explorer-hidden"] == 1,
            hideFileExtensions: _currentValues["explorer-extensions"] == 1,
            startInThisPc: _currentValues["explorer-launch"] == 1,
            showRecentItems: _currentValues["start-recent"] == 1,
            pinTaskbarItem: TryPinTaskbarItem,
            isTaskbarItemPinned: path => _pinnedApps.Any(pin => string.Equals(pin.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)),
            pinStartItem: TryPinStartItem,
            isStartItemPinned: path => _pinnedStartApps.Any(pin => string.Equals(pin.ShortcutPath, path, StringComparison.OrdinalIgnoreCase)))
        { Owner = this };
        _explorerWindow.Closed += (_, _) => _explorerWindow = null;
        _explorerWindow.Show();
        if (!string.IsNullOrWhiteSpace(selectPath)) _explorerWindow.SelectPathInCurrentFolder(selectPath);
    }

    private bool TryPinTaskbarItem(string path)
    {
        var currentPins = _pinnedApps;
        var updatedPins = TaskbarPinCatalog.AddDroppedFiles(currentPins, [path], Directory.Exists);
        if (updatedPins.Count == currentPins.Count)
            return false;

        _pinnedApps = updatedPins;
        SaveDesktopPreferences();
        return true;
    }

    public bool TryPinTaskbarItemFromShell(string path) => TryPinTaskbarItem(path);
    public bool IsTaskbarItemPinnedFromShell(string path) => _pinnedApps.Any(pin => string.Equals(pin.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));

    private bool TryPinStartItem(string path)
    {
        var updatedPins = StartPinCatalog.AddDroppedFiles(_pinnedStartApps, [path]);
        if (updatedPins.Count == _pinnedStartApps.Count)
            return false;

        return SavePinnedStartApps(updatedPins);
    }

    public bool TryPinStartItemFromShell(string path) => TryPinStartItem(path);
    public bool IsStartItemPinnedFromShell(string path) => _pinnedStartApps.Any(pin => string.Equals(pin.ShortcutPath, path, StringComparison.OrdinalIgnoreCase));

    private bool TryOpenLocationInCompanionExplorer(string location)
    {
        var isShellNamespaceLocation = DesktopShellNamespaceCatalog.IsShellNamespaceLocation(location);
        var isFilesystemDirectory = Path.IsPathFullyQualified(location) && Directory.Exists(location);
        if (!ShellHostLaunchPolicy.ShouldRouteStartMenuLocationToCompanionExplorer(_shellHostMode, isFilesystemDirectory, isShellNamespaceLocation))
            return false;

        if (isShellNamespaceLocation)
        {
            OpenShellLocationFromShell(location);
            return true;
        }
        OpenExplorer(Path.GetFullPath(location));
        return true;
    }

    private bool TryOpenFileLocationInCompanionExplorer(string path)
    {
        if (!_shellHostMode || !File.Exists(path)) return false;
        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
        OpenExplorer(folder, Path.GetFullPath(path));
        return true;
    }

    private void OpenShellNamespaceBrowser(string location)
    {
        if (_shellNamespaceBrowserWindow is not null)
        {
            _shellNamespaceBrowserWindow.OpenLocationFromShell(location);
            return;
        }
        _shellNamespaceBrowserWindow = new ShellNamespaceBrowserWindow(location) { Owner = this };
        _shellNamespaceBrowserWindow.Closed += (_, _) => _shellNamespaceBrowserWindow = null;
        _shellNamespaceBrowserWindow.Show();
    }

    private void QuitApplication()
    {
        if (_shellHostMode || _shellOverlayMode)
            Application.Current?.Shutdown();
        else Close();
    }

    private void ToggleWindowsKeyReplacement(CheckBox checkBox, bool enabled)
    {
        var previous = _replaceWindowsKey;
        _replaceWindowsKey = enabled;
        _replaceWindowsKeyPreference = enabled;
        if (!ConfigureWindowsKeyHook())
        {
            _replaceWindowsKey = previous;
            _replaceWindowsKeyPreference = previous;
            ConfigureWindowsKeyHook();
            checkBox.IsChecked = false;
            MessageBox.Show(this, "Windows-key replacement could not be enabled. The native Start menu remains available.", "Could not replace Start key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SaveDesktopPreferences();
        SetStatus(enabled
            ? "The Windows key now opens Desktop Tuner Start while the app is running. Win+key shortcuts still pass through."
            : "The native Windows Start key behavior is restored.");
    }

    private void ToggleExplorerShortcutReplacement(CheckBox checkBox, bool enabled)
    {
        var previous = _replaceExplorerShortcut;
        _replaceExplorerShortcut = enabled;
        if (!ConfigureWindowsKeyHook())
        {
            _replaceExplorerShortcut = previous;
            ConfigureWindowsKeyHook();
            checkBox.IsChecked = previous;
            MessageBox.Show(this, "Win+E could not be routed to Desktop Tuner Explorer. The Windows Explorer shortcut remains available.", "Could not route Win+E", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SaveDesktopPreferences();
        SetStatus(enabled ? "Win+E now opens Desktop Tuner Explorer while the app is running." : "Win+E now opens Windows Explorer.");
    }

    private bool ConfigureWindowsKeyHook()
    {
        if (!_replaceWindowsKey && !_replaceExplorerShortcut)
        {
            _windowsKeyHook?.Dispose();
            _windowsKeyHook = null;
            return true;
        }
        if (_windowsKeyHook is { IsInstalled: true })
        {
            _windowsKeyHook.Dispose();
            _windowsKeyHook = null;
        }
        var hook = new WindowsKeyStartHook(ShowStartMenu, CanActivateTaskbarPinShortcut, ActivateTaskbarPinShortcut, CanFocusTaskbar, FocusTaskbar,
            replaceBareWindowsKey: _replaceWindowsKey, canOpenExplorer: () => _replaceExplorerShortcut, openExplorer: () => OpenExplorer(),
            replaceControlEscape: _shellHostMode || _shellOverlayMode,
            canToggleDesktop: () => _shellHostMode && _taskbarWindows.Any(window => window.IsVisible), toggleDesktop: ToggleShowDesktop,
            canFocusTaskbarSystem: CanFocusTaskbarSystemArea, focusTaskbarSystem: FocusTaskbarSystemArea,
            canOpenPowerUserMenu: CanOpenPowerUserMenu, openPowerUserMenu: OpenPowerUserMenu,
            canOpenRunDialog: CanOpenRunDialog, openRunDialog: ShowRunDialog,
            canMinimizeAllWindows: CanManageShellHostWindows, minimizeAllWindows: MinimizeAllShellWindows,
            canRestoreMinimizedWindows: CanManageShellHostWindows, restoreMinimizedWindows: RestoreShellWindowsMinimizedByShortcut,
            canOpenShellSystemSurface: _ => CanManageShellHostWindows(), openShellSystemSurface: OpenShellSystemSurfaceShortcut,
            canLaunchPinnedAppInstance: CanActivateTaskbarPinShortcut, launchPinnedAppInstance: LaunchTaskbarPinInstanceShortcut,
            canLaunchPinnedAppInstanceAsAdministrator: CanLaunchPinnedAppInstanceAsAdministratorShortcut,
            launchPinnedAppInstanceAsAdministrator: LaunchElevatedTaskbarPinInstanceShortcut,
            canActivateLastActivePinnedApp: CanActivateLastActivePinnedAppShortcut, activateLastActivePinnedApp: ActivateLastActivePinnedAppShortcut);
        if (!hook.TryInstall(out var error))
        {
            hook.Dispose();
            SetStatus($"Could not install the Windows-key hook (Windows error {error}).");
            return false;
        }
        _windowsKeyHook = hook;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr Data;
        public int ByteCount;
        public IntPtr DataPointer;
    }

    private bool CanActivateTaskbarPinShortcut(int oneBasedIndex) =>
        oneBasedIndex >= 1 && oneBasedIndex <= _pinnedApps.Count && _taskbarWindows.Any(window => window.IsVisible);

    private void ActivateTaskbarPinShortcut(int oneBasedIndex)
    {
        var taskbar = _taskbarWindows.FirstOrDefault(window => window.Display.IsPrimary && window.IsVisible)
            ?? _taskbarWindows.FirstOrDefault(window => window.IsVisible);
        taskbar?.TryActivatePinnedApp(oneBasedIndex);
    }

    private void LaunchTaskbarPinInstanceShortcut(int oneBasedIndex)
    {
        var taskbar = _taskbarWindows.FirstOrDefault(window => window.Display.IsPrimary && window.IsVisible)
            ?? _taskbarWindows.FirstOrDefault(window => window.IsVisible);
        taskbar?.TryLaunchPinnedAppInstance(oneBasedIndex);
    }

    private bool CanLaunchPinnedAppInstanceAsAdministratorShortcut(int oneBasedIndex) =>
        CanActivateTaskbarPinShortcut(oneBasedIndex) &&
        TaskbarPinCatalog.CanRunAsAdministrator(_pinnedApps[oneBasedIndex - 1].Name, _pinnedApps[oneBasedIndex - 1].ExecutablePath, _pinnedApps[oneBasedIndex - 1].IsDirectory);

    private void LaunchElevatedTaskbarPinInstanceShortcut(int oneBasedIndex)
    {
        var taskbar = _taskbarWindows.FirstOrDefault(window => window.Display.IsPrimary && window.IsVisible)
            ?? _taskbarWindows.FirstOrDefault(window => window.IsVisible);
        taskbar?.TryLaunchPinnedAppInstance(oneBasedIndex, runAsAdministrator: true);
    }

    private bool CanActivateLastActivePinnedAppShortcut(int oneBasedIndex)
    {
        var taskbar = _taskbarWindows.FirstOrDefault(window => window.Display.IsPrimary && window.IsVisible)
            ?? _taskbarWindows.FirstOrDefault(window => window.IsVisible);
        return taskbar?.CanActivateLastActivePinnedApp(oneBasedIndex, _foregroundWindowHistory.GetMostRecentFirst()) == true;
    }

    private void ActivateLastActivePinnedAppShortcut(int oneBasedIndex)
    {
        var taskbar = _taskbarWindows.FirstOrDefault(window => window.Display.IsPrimary && window.IsVisible)
            ?? _taskbarWindows.FirstOrDefault(window => window.IsVisible);
        taskbar?.TryActivateLastActivePinnedApp(oneBasedIndex, _foregroundWindowHistory.GetMostRecentFirst());
    }

    private bool CanFocusTaskbar() => _taskbarWindows.Any(window => window.IsVisible);

    private bool CanFocusTaskbarSystemArea() => _shellHostMode && _taskbarWindows.Any(window => window.IsVisible);

    private void FocusTaskbarSystemArea()
    {
        if (!CanFocusTaskbarSystemArea()) return;
        if (_startMenuWindow?.IsVisible == true) _startMenuWindow.Close();
        var foregroundWindow = GetForegroundWindow();
        var foregroundDisplay = foregroundWindow != 0
            ? TaskbarDisplayService.GetDeviceNameForWindow(foregroundWindow)
            : null;
        var targetDisplay = TaskbarKeyboardNavigationPolicy.SelectForForeground(
            _taskbarWindows.Where(window => window.IsVisible).Select(window => window.Display), foregroundDisplay);
        var taskbar = targetDisplay is null
            ? null
            : _taskbarWindows.FirstOrDefault(window => string.Equals(window.Display.DeviceName, targetDisplay.DeviceName, StringComparison.OrdinalIgnoreCase) && window.IsVisible);
        if (taskbar is null) return;
        if (!_taskbarWindows.Any(window => window.HasKeyboardTaskbarFocus))
            _taskbarFocusReturnWindow = GetForegroundWindow();
        taskbar.FocusTaskbarSystemArea(_taskbarFocusReturnWindow);
    }

    private bool CanOpenPowerUserMenu() => _shellHostMode && _taskbarWindows.Any(window => window.IsVisible);

    private void OpenPowerUserMenu()
    {
        if (!CanOpenPowerUserMenu()) return;
        if (_startMenuWindow?.IsVisible == true) _startMenuWindow.Close();
        var taskbar = _taskbarWindows.FirstOrDefault(window => window.Display.IsPrimary && window.IsVisible)
            ?? _taskbarWindows.FirstOrDefault(window => window.IsVisible);
        taskbar?.ShowPowerUserMenu();
    }

    private bool CanOpenRunDialog() => _shellHostMode && _taskbarWindows.Any(window => window.IsVisible);

    private bool CanManageShellHostWindows() => _shellHostMode && _taskbarWindows.Any(window => window.IsVisible);

    private List<nint> GetShellSurfaceHandles()
    {
        var handles = _taskbarWindows.Select(window => new WindowInteropHelper(window).Handle).ToList();
        if (Application.Current?.MainWindow is { } desktopHost)
            handles.Add(new WindowInteropHelper(desktopHost).Handle);
        return handles;
    }

    private void MinimizeAllShellWindows()
    {
        if (!CanManageShellHostWindows()) return;
        if (_startMenuWindow?.IsVisible == true) _startMenuWindow.Close();
        _showDesktopWindows.MinimizeAllWindows(GetShellSurfaceHandles());
    }

    private void RestoreShellWindowsMinimizedByShortcut()
    {
        if (!CanManageShellHostWindows()) return;
        _showDesktopWindows.RestoreMinimizedWindows();
    }

    private void OpenShellSystemSurfaceShortcut(uint key)
    {
        if (!CanManageShellHostWindows()) return;
        switch (key)
        {
            case (uint)'A': SystemFlyoutService.OpenQuickSettings(); break;
            case (uint)'N': SystemFlyoutService.OpenNotificationCenter(); break;
            case (uint)'S': SystemFlyoutService.OpenWindowsSearch(); break;
            case (uint)'W': SystemFlyoutService.OpenWidgets(); break;
            case 0x20: SystemFlyoutService.OpenInputMethodSwitcher(); break;
            case 0x09: SystemFlyoutService.OpenTaskView(); break;
        }
    }

    private void ShowRunDialog()
    {
        if (!CanOpenRunDialog()) return;
        if (_startMenuWindow?.IsVisible == true) _startMenuWindow.Close();
        var dialog = new RunDialogWindow(ExecuteRunCommand);
        dialog.Show();
        dialog.Activate();
    }

    private void ExecuteRunCommand(string commandLine, bool runAsAdministrator)
    {
        var startInfo = RunCommandService.CreateStartInfo(commandLine, runAsAdministrator);
        if (startInfo.ArgumentList.Count == 0 && DesktopShellNamespaceCatalog.IsShellNamespaceLocation(startInfo.FileName))
        {
            if (runAsAdministrator)
                throw new InvalidOperationException("Shell namespace locations open in Desktop Tuner Explorer and cannot be elevated from this dialog.");
            OpenShellLocationFromShell(startInfo.FileName);
            return;
        }
        if (startInfo.ArgumentList.Count == 0 && Directory.Exists(startInfo.FileName))
        {
            if (runAsAdministrator)
                throw new InvalidOperationException("Folders open in Desktop Tuner Explorer and cannot be elevated from this dialog.");
            OpenExplorer(Path.GetFullPath(startInfo.FileName));
            return;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start the requested program or open the requested location.");
    }

    private void ExecuteShellHostPowerUserCommand(string commandId)
    {
        if (commandId.StartsWith("power:", StringComparison.OrdinalIgnoreCase))
        {
            var action = StartPowerActionCatalog.ById(commandId["power:".Length..]);
            if (action.RequiresConfirmation)
            {
                var choice = MessageBox.Show(this,
                    $"Are you sure you want to {action.Label.ToLowerInvariant()}? Save your work in open apps first.",
                    action.Label, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (choice != MessageBoxResult.Yes) return;
            }

            try { StartPowerActionService.Execute(action.Id); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, $"Could not {action.Label.ToLowerInvariant()}", MessageBoxButton.OK, MessageBoxImage.Error); }
            return;
        }

        try
        {
            switch (commandId)
            {
                case "explorer":
                    OpenExplorer();
                    return;
                case "search":
                    ShowStartMenu();
                    return;
                case "run":
                    ShowRunDialog();
                    return;
                case "desktop":
                    ToggleShowDesktop();
                    return;
                case "restart-shell":
                    var restartAnswer = MessageBox.Show(this,
                        "Restart Desktop Tuner's shell replacement? The shell supervisor will start it again once.",
                        "Restart shell replacement", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (restartAnswer == MessageBoxResult.Yes)
                        Application.Current?.Shutdown(ShellHostLaunchPolicy.RequestedRestartExitCode);
                    return;
                case "exit-shell":
                    var answer = MessageBox.Show(this,
                        "Exit Desktop Tuner's shell replacement and start Windows Explorer for this session?",
                        "Exit shell replacement", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (answer == MessageBoxResult.Yes) Application.Current?.Shutdown();
                    return;
                default:
                    AppCatalogService.OpenLocation(ShellHostPowerMenuCatalog.SystemCommand(commandId).Target!);
                    return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open system tool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleShowDesktop()
    {
        if (!_shellHostMode) return;
        if (_startMenuWindow?.IsVisible == true) _startMenuWindow.Close();
        _showDesktopWindows.Toggle(GetShellSurfaceHandles());
    }

    private void FocusTaskbar(bool forward = true)
    {
        if (_startMenuWindow?.IsVisible == true) _startMenuWindow.Close();
        var visibleTaskbars = TaskbarKeyboardNavigationPolicy.OrderDisplays(
                _taskbarWindows.Where(window => window.IsVisible).Select(window => window.Display))
            .Select(display => _taskbarWindows.First(window => window.IsVisible &&
                string.Equals(window.Display.DeviceName, display.DeviceName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (visibleTaskbars.Length == 0) return;

        var focusedIndex = Array.FindIndex(visibleTaskbars, window => window.HasKeyboardTaskbarFocus);
        if (focusedIndex < 0) _taskbarFocusReturnWindow = GetForegroundWindow();

        var nextIndex = focusedIndex < 0
            ? forward ? 0 : visibleTaskbars.Length - 1
            : focusedIndex;
        var startAtEdge = false;
        if (focusedIndex >= 0 && visibleTaskbars[focusedIndex].IsAtKeyboardFocusBoundary(forward) && visibleTaskbars.Length > 1)
        {
            nextIndex = TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(focusedIndex, visibleTaskbars.Length, forward)!.Value;
            startAtEdge = true;
        }

        visibleTaskbars[nextIndex].FocusTaskbar(forward, startAtEdge, _taskbarFocusReturnWindow);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendCopyData(IntPtr window, uint message, IntPtr wParam, ref CopyDataStruct data, uint flags, uint timeout, out IntPtr result);
}
