namespace DesktopTuner;

public enum TaskbarSystemButton
{
    Settings,
    Tray,
    QuickSettings,
    Network,
    InputMethod,
    OnScreenKeyboard,
    Emoji,
    Volume,
    Battery,
    Widgets,
    TaskView,
    ShowDesktop,
    Clock
}

public sealed record TaskbarSystemButtonOption(TaskbarSystemButton Button, string Label, string Description);

public sealed record TaskbarSystemButtonVisibility(
    bool Settings = true,
    bool Tray = true,
    bool QuickSettings = true,
    bool Network = true,
    bool Emoji = true,
    bool Volume = true,
    bool Battery = true,
    bool Widgets = true,
    bool TaskView = true,
    bool ShowDesktop = true,
    bool Clock = true)
{
    public bool InputMethod { get; init; } = true;
    public bool OnScreenKeyboard { get; init; } = true;

    public static TaskbarSystemButtonVisibility Default { get; } = new();

    public static IReadOnlyList<TaskbarSystemButtonOption> Options { get; } = Array.AsReadOnly<TaskbarSystemButtonOption>(
    [
        new(TaskbarSystemButton.Settings, "Settings", "Open Desktop Tuner settings."),
        new(TaskbarSystemButton.Tray, "Notification area", "Focus the Windows notification area."),
        new(TaskbarSystemButton.QuickSettings, "Quick Settings", "Open Windows Quick Settings."),
        new(TaskbarSystemButton.Network, "Network", "Open Wi-Fi and network settings."),
        new(TaskbarSystemButton.InputMethod, "Keyboard layout", "Open the Windows keyboard layout and input method picker."),
        new(TaskbarSystemButton.OnScreenKeyboard, "On-Screen Keyboard", "Open the Windows On-Screen Keyboard."),
        new(TaskbarSystemButton.Emoji, "Emoji panel", "Open the Windows emoji panel."),
        new(TaskbarSystemButton.Volume, "Volume", "Adjust volume and select an audio output."),
        new(TaskbarSystemButton.Battery, "Battery", "Show battery status when available."),
        new(TaskbarSystemButton.Widgets, "Widgets", "Open the Windows Widgets board."),
        new(TaskbarSystemButton.TaskView, "Task View", "Open Windows Task View."),
        new(TaskbarSystemButton.ShowDesktop, "Show desktop", "Minimize windows to show the desktop."),
        new(TaskbarSystemButton.Clock, "Clock and notifications", "Show the clock and open notifications.")
    ]);

    public static TaskbarSystemButtonVisibility Normalize(TaskbarSystemButtonVisibility? value) => value ?? Default;

    public bool IsVisible(TaskbarSystemButton button) => button switch
    {
        TaskbarSystemButton.Settings => Settings,
        TaskbarSystemButton.Tray => Tray,
        TaskbarSystemButton.QuickSettings => QuickSettings,
        TaskbarSystemButton.Network => Network,
        TaskbarSystemButton.InputMethod => InputMethod,
        TaskbarSystemButton.OnScreenKeyboard => OnScreenKeyboard,
        TaskbarSystemButton.Emoji => Emoji,
        TaskbarSystemButton.Volume => Volume,
        TaskbarSystemButton.Battery => Battery,
        TaskbarSystemButton.Widgets => Widgets,
        TaskbarSystemButton.TaskView => TaskView,
        TaskbarSystemButton.ShowDesktop => ShowDesktop,
        TaskbarSystemButton.Clock => Clock,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown taskbar system button.")
    };

    public TaskbarSystemButtonVisibility WithVisibility(TaskbarSystemButton button, bool isVisible) => button switch
    {
        TaskbarSystemButton.Settings => this with { Settings = isVisible },
        TaskbarSystemButton.Tray => this with { Tray = isVisible },
        TaskbarSystemButton.QuickSettings => this with { QuickSettings = isVisible },
        TaskbarSystemButton.Network => this with { Network = isVisible },
        TaskbarSystemButton.InputMethod => this with { InputMethod = isVisible },
        TaskbarSystemButton.OnScreenKeyboard => this with { OnScreenKeyboard = isVisible },
        TaskbarSystemButton.Emoji => this with { Emoji = isVisible },
        TaskbarSystemButton.Volume => this with { Volume = isVisible },
        TaskbarSystemButton.Battery => this with { Battery = isVisible },
        TaskbarSystemButton.Widgets => this with { Widgets = isVisible },
        TaskbarSystemButton.TaskView => this with { TaskView = isVisible },
        TaskbarSystemButton.ShowDesktop => this with { ShowDesktop = isVisible },
        TaskbarSystemButton.Clock => this with { Clock = isVisible },
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown taskbar system button.")
    };
}
