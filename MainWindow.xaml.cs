using Microsoft.Win32;
using System.IO;
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
    private TaskbarDisplay? _startMenuDisplay;
    private readonly List<TaskbarWindow> _taskbarWindows = [];
    private readonly TaskbarWindowOrder _taskbarWindowOrder = new();
    private readonly NativeTaskbarVisibilityService _nativeTaskbarVisibility = new();
    private readonly DispatcherTimer _nativeTaskbarWatchTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private WindowsKeyStartHook? _windowsKeyHook;
    private TaskbarEdge _taskbarEdge = TaskbarEdge.Bottom;
    private TaskbarSize _taskbarSize = TaskbarSize.Standard;
    private TaskbarStyle _taskbarLayout = TaskbarStyle.EdgeToEdge;
    private TaskbarGroupingMode _taskbarGrouping = TaskbarGroupingMode.Always;
    private TaskbarButtonAlignment _taskbarButtonAlignment = TaskbarButtonAlignment.Center;
    private TaskbarIconSize _taskbarIconSize = TaskbarIconSize.Standard;
    private TaskbarButtonSpacing _taskbarButtonSpacing = TaskbarButtonSpacing.Standard;
    private bool _taskbarShowLabels = true;
    private bool _taskbarAutoHide;
    private bool _taskbarAutoHideWhenMaximized;
    private int _taskbarTransparency = 5;
    private List<PinnedTaskbarApp> _pinnedApps = [];
    private List<AppEntry> _pinnedStartApps = [];
    private bool _replaceWindowsKey;
    private StartMenuStyle _startMenuStyle = StartMenuStyle.Modern;
    private bool _taskbarOnAllDisplays = true;
    private bool _replaceNativeTaskbar;
    private bool _startWithWindows;
    private readonly bool _startInBackground;
    private bool _closingTaskbars;
    private bool _displayRefreshPending;

    public MainWindow(bool startInBackground = false)
    {
        InitializeComponent();
        _startInBackground = startInBackground;
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
        _settings = new RegistrySettingsService(_profileStore);
        var desktopPreferences = _preferences.Load();
        _taskbarEdge = desktopPreferences.TaskbarEdge;
        _taskbarSize = desktopPreferences.TaskbarSize;
        _taskbarLayout = desktopPreferences.TaskbarLayout;
        _taskbarIconSize = desktopPreferences.TaskbarIconSize;
        _taskbarButtonSpacing = desktopPreferences.TaskbarButtonSpacing;
        _taskbarShowLabels = desktopPreferences.TaskbarShowLabels;
        _taskbarAutoHide = desktopPreferences.AutoHide;
        _taskbarAutoHideWhenMaximized = desktopPreferences.AutoHideWhenMaximized;
        _taskbarTransparency = desktopPreferences.TaskbarTransparency;
        _pinnedApps = desktopPreferences.PinnedApps ?? [];
        _pinnedStartApps = StartPinCatalog.Normalize(desktopPreferences.PinnedStartApps).ToList();
        _replaceWindowsKey = desktopPreferences.ReplaceWindowsKey;
        _startMenuStyle = desktopPreferences.StartMenuStyle;
        _taskbarOnAllDisplays = desktopPreferences.TaskbarOnAllDisplays;
        _replaceNativeTaskbar = desktopPreferences.ReplaceNativeTaskbar;
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
        PageContent.Children.Add(new TextBlock { Text = "Make Windows feel like yours.", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brush("#172033") });
        PageContent.Children.Add(new TextBlock { Text = "Tune the Start menu, taskbar, and File Explorer in one place. Changes are per-user and reversible.", FontSize = 14, Foreground = Brush("#697386"), Margin = new Thickness(0, 8, 0, 24) });

        var hero = new Border { Background = Brush("#232A3D"), CornerRadius = new CornerRadius(14), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 22) };
        var heroStack = new StackPanel();
        heroStack.Children.Add(new TextBlock { Text = "YOUR WINDOWS, RECLAIMED", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("#B9B4FF") });
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
        noteStack.Children.Add(new TextBlock { Text = "Windows can change shell behavior between releases. Taskbar controls are marked experimental and may need an Explorer restart or sign-out on some builds. This app never injects code into Explorer.", Foreground = Brush("#697386"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        note.Child = noteStack;
        PageContent.Children.Add(note);
    }

    private void AddModuleCard(Panel parent, string title, string description, string page, string number)
    {
        var card = Card();
        card.Margin = new Thickness(0, 0, 12, 0);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = number, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("#6258D9") });
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 5) });
        stack.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Brush("#697386"), TextWrapping = TextWrapping.Wrap, MinHeight = 48 });
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
            var replaceStart = new CheckBox { Content = "Use Desktop Tuner Start menu for the Windows key while this app is running", IsChecked = _replaceWindowsKey, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            replaceStart.Checked += (_, _) => ToggleWindowsKeyReplacement(replaceStart, true);
            replaceStart.Unchecked += (_, _) => ToggleWindowsKeyReplacement(replaceStart, false);
            PageContent.Children.Add(replaceStart);
            var info = InfoCard("Windows-key integration", "When enabled, tapping either Windows key opens Desktop Tuner Start while this app is running. Win+key combinations such as Win+R are forwarded to Windows. Turn this off at any time to restore the native Start key.");
            PageContent.Children.Add(info);
        }
        else if (section == "Explorer")
        {
            var explorerButton = new Button { Content = "Open Desktop Tuner Explorer", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) };
            explorerButton.Click += (_, _) => OpenExplorer();
            PageContent.Children.Add(explorerButton);
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

            var showLabels = new CheckBox { Content = "Show app names on taskbar buttons", IsChecked = _taskbarShowLabels, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            showLabels.Checked += (_, _) => { _taskbarShowLabels = true; SaveDesktopPreferences(); };
            showLabels.Unchecked += (_, _) => { _taskbarShowLabels = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(showLabels);

            var styleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            styleRow.Children.Add(new TextBlock { Text = "Taskbar style", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) });
            var styleSelector = new ComboBox { Width = 190, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            styleSelector.Items.Add(new ComboBoxItem { Content = "Full edge", Tag = TaskbarStyle.EdgeToEdge });
            styleSelector.Items.Add(new ComboBoxItem { Content = "Floating", Tag = TaskbarStyle.Floating });
            styleSelector.Items.Add(new ComboBoxItem { Content = "Segmented", Tag = TaskbarStyle.Segmented });
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

            var autoHide = new CheckBox { Content = "Automatically hide the custom taskbar", IsChecked = _taskbarAutoHide, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            autoHide.Checked += (_, _) => { _taskbarAutoHide = true; SaveDesktopPreferences(); };
            autoHide.Unchecked += (_, _) => { _taskbarAutoHide = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(autoHide);
            var autoHideMaximized = new CheckBox { Content = "Hide the taskbar when a maximized app covers this display", IsChecked = _taskbarAutoHideWhenMaximized, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            autoHideMaximized.Checked += (_, _) => { _taskbarAutoHideWhenMaximized = true; SaveDesktopPreferences(); };
            autoHideMaximized.Unchecked += (_, _) => { _taskbarAutoHideWhenMaximized = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(autoHideMaximized);
            var allDisplays = new CheckBox { Content = "Show the custom taskbar on all displays", IsChecked = _taskbarOnAllDisplays, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            allDisplays.Checked += (_, _) => { _taskbarOnAllDisplays = true; SaveDesktopPreferences(); };
            allDisplays.Unchecked += (_, _) => { _taskbarOnAllDisplays = false; SaveDesktopPreferences(); };
            PageContent.Children.Add(allDisplays);
            var replaceNativeTaskbar = new CheckBox
            {
                Content = "Replace the Windows taskbar while Desktop Tuner is running (experimental)",
                IsChecked = _replaceNativeTaskbar,
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
                Text = "Hides the built-in taskbar on displays covered by Desktop Tuner and restores it when the app exits. If Desktop Tuner crashes, restart Windows Explorer or sign out to restore the built-in taskbar.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("DesktopMutedTextBrush"),
                Margin = new Thickness(0, 0, 0, 16)
            });
            var startWithWindows = new CheckBox { Content = "Start the taskbar automatically when I sign in", IsChecked = _startWithWindows, Margin = new Thickness(0, 0, 0, 16), FontSize = 13 };
            startWithWindows.Checked += (_, _) => SetStartWithWindows(startWithWindows, true);
            startWithWindows.Unchecked += (_, _) => SetStartWithWindows(startWithWindows, false);
            PageContent.Children.Add(startWithWindows);
            var launchButton = new Button { Content = _replaceNativeTaskbar ? "Start replacement taskbar" : "Open Desktop Tuner taskbar overlay", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) };
            launchButton.Click += (_, _) => ShowTaskbar();
            PageContent.Children.Add(launchButton);
            var overlayInfo = InfoCard(_replaceNativeTaskbar ? "Experimental taskbar replacement" : "Live taskbar overlay", "Choose an edge, bar size and style, transparency, app button labels, icon size, spacing, and optional auto-hide. The custom taskbar lists open windows, activates or minimizes them, opens the companion Start menu on the same display, and opens the native Widgets board. Enable sign-in startup to keep the taskbar running in the background; right-click the bar to reopen Desktop Tuner settings or exit. In replacement mode, the built-in taskbar is hidden only on displays covered by Desktop Tuner and restored when its windows close. Otherwise, the overlay can leave Windows' native notification area visible on supported bottom layouts.");
            PageContent.Children.Add(overlayInfo);
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
            text.Children.Add(new TextBlock { Text = setting.Description, FontSize = 12, Foreground = Brush("#697386"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
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
        stack.Children.Add(new TextBlock { Text = "Profiles are JSON files that contain only the app's known setting choices. Importing a file selects its values; it does not apply them automatically.", FontSize = 12, Foreground = Brush("#697386"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16) });
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
        restoreStack.Children.Add(new TextBlock { Text = "Undo last apply uses the snapshot from immediately before the last successful apply. It restores missing registry values to their original absent state as well.", FontSize = 12, Foreground = Brush("#697386"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        restore.Child = restoreStack;
        PageContent.Children.Add(restore);
    }

    private void AddPageHeading(string title, string subtitle)
    {
        PageContent.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold });
        PageContent.Children.Add(new TextBlock { Text = subtitle, FontSize = 14, Foreground = Brush("#697386"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 23) });
    }

    private Border InfoCard(string title, string description)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("#4943A2") });
        stack.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Brush("#545F73"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        var card = Card();
        card.Background = Brush("#F0EFFF");
        card.BorderBrush = Brush("#DDD9FF");
        card.Margin = new Thickness(0, 0, 0, 15);
        card.Child = stack;
        return card;
    }

    private static Border Card() => new() { Background = Brushes.White, BorderBrush = Brush("#E7EAF0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18) };
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
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource.AddHook(WindowMessageHook);
        var unavailableShortcuts = new List<string>();
        if (!RegisterHotKey(handle, StartMenuHotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_SPACE))
            unavailableShortcuts.Add("Ctrl+Alt+Space");
        if (!RegisterHotKey(handle, TaskbarAutoHideHotkeyId, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_T))
            unavailableShortcuts.Add("Win+Alt+T");
        if (unavailableShortcuts.Count > 0)
            SetStatus($"Global shortcut(s) {string.Join(" and ", unavailableShortcuts)} unavailable; use the app buttons or taskbar menu instead.");
        if (_replaceWindowsKey && !EnableWindowsKeyHook())
        {
            _replaceWindowsKey = false;
            SaveDesktopPreferences();
            SetStatus("Windows-key replacement could not start; the setting was turned off.");
        }
        if (_startInBackground && _startWithWindows)
        {
            Hide();
            Dispatcher.BeginInvoke(new Action(ShowTaskbar));
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        CloseTaskbars();
        _nativeTaskbarWatchTimer.Stop();
        _nativeTaskbarVisibility.Restore();
        var handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, StartMenuHotkeyId);
        UnregisterHotKey(handle, TaskbarAutoHideHotkeyId);
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowsKeyHook?.Dispose();
        _windowsKeyHook = null;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WM_DWMCOLORIZATIONCOLORCHANGED)
            DesktopTheme.Apply(_currentValues["explorer-app-mode"] == 0);
        if (message == WM_APP_ACTIVATE_SETTINGS)
        {
            ShowSettingsWindow();
            handled = true;
            return IntPtr.Zero;
        }
        if (message == WM_DISPLAYCHANGE && !_displayRefreshPending && _taskbarWindows.Any(window => window.IsVisible))
        {
            _displayRefreshPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _displayRefreshPending = false;
                if (!_taskbarWindows.Any(window => window.IsVisible)) return;
                CloseTaskbars();
                ShowTaskbar();
            }));
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
            foreach (var taskbar in _taskbarWindows.ToArray()) taskbar.SetPreferences(preferences);
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

    private void ShowStartMenu() => ShowStartMenu(null);

    private void ShowStartMenu(TaskbarDisplay? display)
    {
        if (_startMenuWindow is { IsVisible: true })
        {
            _startMenuWindow.Close();
            return;
        }
        _startMenuWindow = new StartMenuWindow(_startMenuStyle, _pinnedStartApps, SavePinnedStartApps);
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
                new DesktopPreferences(_taskbarEdge, _taskbarSize, _taskbarAutoHide, TaskbarLayout: _taskbarLayout, AutoHideWhenMaximized: _taskbarAutoHideWhenMaximized));
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
            var preferences = CreateDesktopPreferences();
            foreach (var display in TaskbarDisplayService.Select(_taskbarOnAllDisplays))
            {
                var taskbar = new TaskbarWindow(display, targetDisplay => ShowStartMenu(targetDisplay), () => _startMenuWindow?.IsVisible == true, preferences, _taskbarWindowOrder, SaveDesktopPreferences, CloseTaskbars, ShowSettingsWindow, QuitApplication);
                taskbar.Closed += (_, _) =>
                {
                    _taskbarWindows.Remove(taskbar);
                    if (!_closingTaskbars) CloseTaskbars();
                };
                _taskbarWindows.Add(taskbar);
                taskbar.Show();
            }
            if (_replaceNativeTaskbar)
            {
                var displays = TaskbarDisplayService.Select(_taskbarOnAllDisplays);
                if (!_nativeTaskbarVisibility.HideForDisplays(displays))
                {
                    throw new InvalidOperationException("Windows did not expose a taskbar on the selected displays, so replacement mode could not start.");
                }
                _nativeTaskbarWatchTimer.Start();
            }
            SetStatus(_taskbarOnAllDisplays
                ? _replaceNativeTaskbar
                    ? "Desktop Tuner taskbar replacement is running on all displays. Close it to restore Windows taskbars."
                    : "Desktop Tuner taskbar overlays are running on all displays. Close one to reveal the Windows taskbar everywhere."
                : _replaceNativeTaskbar
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
        }
    }

    private void CloseTaskbars()
    {
        if (_closingTaskbars) return;
        _closingTaskbars = true;
        try
        {
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
        if (!_replaceNativeTaskbar || !_taskbarWindows.Any(window => window.IsVisible))
        {
            _nativeTaskbarWatchTimer.Stop();
            _nativeTaskbarVisibility.Restore();
            return;
        }

        try
        {
            _nativeTaskbarVisibility.HideForDisplays(TaskbarDisplayService.Select(_taskbarOnAllDisplays));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Could not keep native taskbars hidden during replacement mode: {ex}");
        }
    }

    private DesktopPreferences CreateDesktopPreferences() => new(_taskbarEdge, _taskbarSize, _taskbarAutoHide, _pinnedApps.ToList(), _replaceWindowsKey, _startMenuStyle, _taskbarOnAllDisplays, _taskbarLayout, _taskbarGrouping, _taskbarButtonAlignment, _taskbarShowLabels, _taskbarIconSize, _taskbarButtonSpacing, _startWithWindows, _taskbarAutoHideWhenMaximized, _taskbarTransparency, _pinnedStartApps.ToList(), _replaceNativeTaskbar);

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
        foreach (var taskbar in _taskbarWindows.ToArray()) taskbar.SetPreferences(preferences);
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
            _pinnedApps = preferences.PinnedApps ?? [];
            _pinnedStartApps = StartPinCatalog.Normalize(preferences.PinnedStartApps).ToList();
            _replaceWindowsKey = preferences.ReplaceWindowsKey;
            _startMenuStyle = preferences.StartMenuStyle;
            _taskbarOnAllDisplays = preferences.TaskbarOnAllDisplays;
            _taskbarLayout = preferences.TaskbarLayout;
            _taskbarGrouping = preferences.TaskbarGrouping;
            _taskbarButtonAlignment = preferences.TaskbarButtonAlignment;
            _taskbarShowLabels = preferences.TaskbarShowLabels;
            _taskbarIconSize = preferences.TaskbarIconSize;
            _taskbarButtonSpacing = preferences.TaskbarButtonSpacing;
            _startWithWindows = preferences.StartWithWindows;
            _replaceNativeTaskbar = preferences.ReplaceNativeTaskbar;
            if ((displayModeChanged || replacementModeChanged) && _taskbarWindows.Any(window => window.IsVisible))
            {
                CloseTaskbars();
                ShowTaskbar();
            }
            else
            {
                foreach (var taskbar in _taskbarWindows.ToArray()) taskbar.SetPreferences(preferences);
            }
            _startMenuWindow?.SetStyle(_startMenuStyle);
            SetStatus("Desktop preferences saved.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save taskbar preferences", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SetStartWithWindows(CheckBox checkBox, bool enabled)
    {
        if (_startWithWindows == enabled) return;
        try
        {
            StartupShortcutService.SetEnabled(enabled);
            _preferences.Save(CreateDesktopPreferences() with { StartWithWindows = enabled });
            _startWithWindows = enabled;
            SetStatus(enabled ? "The taskbar will start automatically at sign-in." : "Automatic taskbar startup is disabled.");
        }
        catch (Exception ex)
        {
            try { StartupShortcutService.SetEnabled(_startWithWindows); }
            catch (Exception rollbackError) { ex = new AggregateException("The Startup shortcut could not be restored after saving failed.", ex, rollbackError); }
            checkBox.IsChecked = _startWithWindows;
            MessageBox.Show(this, ex.Message, "Could not change sign-in startup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowSettingsWindow()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        RenderPage("Taskbar");
        Activate();
    }

    private void OpenExplorer()
    {
        if (_explorerWindow is { IsVisible: true })
        {
            _explorerWindow.Activate();
            return;
        }

        _explorerWindow = new ExplorerWindow(
            showHiddenItems: _currentValues["explorer-hidden"] == 1,
            hideFileExtensions: _currentValues["explorer-extensions"] == 1,
            startInThisPc: _currentValues["explorer-launch"] == 1,
            showRecentItems: _currentValues["start-recent"] == 1)
        { Owner = this };
        _explorerWindow.Closed += (_, _) => _explorerWindow = null;
        _explorerWindow.Show();
    }

    private void QuitApplication() => Close();

    private void ToggleWindowsKeyReplacement(CheckBox checkBox, bool enabled)
    {
        if (enabled && !EnableWindowsKeyHook())
        {
            checkBox.IsChecked = false;
            MessageBox.Show(this, "Windows-key replacement could not be enabled. The native Start menu remains available.", "Could not replace Start key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!enabled)
        {
            _windowsKeyHook?.Dispose();
            _windowsKeyHook = null;
        }
        _replaceWindowsKey = enabled;
        SaveDesktopPreferences();
        SetStatus(enabled
            ? "The Windows key now opens Desktop Tuner Start while the app is running. Win+key shortcuts still pass through."
            : "The native Windows Start key behavior is restored.");
    }

    private bool EnableWindowsKeyHook()
    {
        if (_windowsKeyHook?.IsInstalled == true) return true;
        var hook = new WindowsKeyStartHook(ShowStartMenu);
        if (!hook.TryInstall(out var error))
        {
            hook.Dispose();
            SetStatus($"Could not install the Windows-key hook (Windows error {error}).");
            return false;
        }
        _windowsKeyHook = hook;
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
