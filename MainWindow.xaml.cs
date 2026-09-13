using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public partial class MainWindow : Window
{
    private const int StartMenuHotkeyId = 0xD701;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_SPACE = 0x20;
    private readonly ProfileStore _profileStore = new();
    private readonly RegistrySettingsService _settings;
    private readonly Dictionary<string, ComboBox> _controls = [];
    private readonly Dictionary<string, int> _currentValues = [];
    private readonly Dictionary<string, int> _selectedValues = [];
    private readonly HashSet<string> _dirty = [];
    private string _activePage = "Overview";
    private HwndSource? _windowSource;
    private StartMenuWindow? _startMenuWindow;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
        _settings = new RegistrySettingsService(_profileStore);
        foreach (var setting in SettingsCatalog.All)
        {
            var value = _settings.Read(setting);
            _currentValues[setting.Id] = value;
            _selectedValues[setting.Id] = value;
        }
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
        AddModuleCard(grid, "Taskbar", "Alignment and window grouping preferences.", "Taskbar", "02");
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
            var info = InfoCard("Privacy setting shared with Windows", "The recent-items preference affects Start, Jump Lists, and File Explorer together. Windows does not expose an app API to independently rebuild the built-in Start menu layout.");
            PageContent.Children.Add(info);
        }
        if (section == "Taskbar")
        {
            var info = InfoCard("Experimental Windows setting", "Microsoft may change or ignore these taskbar registry preferences in a future Windows release. The app stores the previous values so you can undo its last apply.");
            PageContent.Children.Add(info);
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
            _dirty.Clear();
            SetStatus("Changes applied. Some taskbar options may need Explorer to restart or Windows to sign out and back in.");
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
            _dirty.Clear();
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
        if (!RegisterHotKey(handle, StartMenuHotkeyId, MOD_CONTROL | MOD_ALT, VK_SPACE))
            SetStatus("Global shortcut Ctrl+Alt+Space is unavailable. Open the Start page and use its launcher button.");
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, StartMenuHotkeyId);
        _windowSource?.RemoveHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WM_HOTKEY && wParam.ToInt32() == StartMenuHotkeyId)
        {
            ShowStartMenu();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ShowStartMenu()
    {
        if (_startMenuWindow is { IsVisible: true })
        {
            _startMenuWindow.Close();
            return;
        }
        _startMenuWindow = new StartMenuWindow();
        _startMenuWindow.Closed += (_, _) => _startMenuWindow = null;
        var workArea = SystemParameters.WorkArea;
        _startMenuWindow.Left = workArea.Left + 12;
        _startMenuWindow.Top = Math.Max(workArea.Top + 12, workArea.Bottom - _startMenuWindow.Height - 12);
        _startMenuWindow.Show();
        _startMenuWindow.Activate();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
