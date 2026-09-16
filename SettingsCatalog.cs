namespace DesktopTuner;

public sealed record SettingChoice(string Label, int Value, string? RegistryString = null, bool DeleteRegistryValue = false);

public sealed record SettingDefinition(
    string Id,
    string Section,
    string Name,
    string Description,
    string RegistryPath,
    string ValueName,
    IReadOnlyList<SettingChoice> Choices,
    int DefaultValue,
    bool Experimental = false);

public static class SettingsCatalog
{
    public const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string ExplorerRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    public const string ExplorerCabinetState = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState";
    public const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string Accessibility = @"Control Panel\Accessibility";

    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        new("start-recent", "Start", "Recent items", "Control whether Windows tracks recent items in Start, Jump Lists, and File Explorer.", ExplorerAdvanced, "Start_TrackDocs",
            [new("Show recent items", 1), new("Hide recent items", 0)], 1),
        new("taskbar-alignment", "Taskbar", "Taskbar alignment", "Choose where the Start button and taskbar icons sit.", ExplorerAdvanced, "TaskbarAl",
            [new("Left", 0), new("Center", 1)], 1, true),
        new("taskbar-combine", "Taskbar", "Combine taskbar buttons", "Choose whether windows from the same app share one taskbar button.", ExplorerAdvanced, "TaskbarGlomLevel",
            [new("Always", 0), new("When taskbar is full", 1), new("Never", 2)], 0, true),
        new("explorer-launch", "Explorer", "Open File Explorer to", "Choose the first page shown in a new File Explorer window.", ExplorerAdvanced, "LaunchTo",
            [new("Home", 2), new("This PC", 1)], 2),
        new("explorer-full-path", "Explorer", "Full path in title bar", "Display the full folder path in the title bar where File Explorer supports it.", ExplorerCabinetState, "FullPath",
            [new("Show full path", 1), new("Show folder name", 0)], 0, true),
        new("explorer-separate-process", "Explorer", "Separate folder processes", "Open folder windows in separate Explorer processes to isolate crashes.", ExplorerAdvanced, "SeparateProcess",
            [new("Enabled", 1), new("Disabled", 0)], 0, true),
        new("explorer-extensions", "Explorer", "File name extensions", "Show or hide extensions such as .txt and .png.", ExplorerAdvanced, "HideFileExt",
            [new("Show extensions", 0), new("Hide extensions", 1)], 1),
        new("explorer-hidden", "Explorer", "Hidden files", "Show or hide files marked as hidden.", ExplorerAdvanced, "Hidden",
            [new("Show hidden files", 1), new("Hide hidden files", 2)], 2),
        new("explorer-compact", "Explorer", "Item spacing", "Use compact spacing in File Explorer lists.", ExplorerAdvanced, "UseCompactMode",
            [new("Compact", 1), new("Comfortable", 0)], 0),
        new("explorer-context-menu", "Explorer", "Context menu style", "Switch between the compact Windows 11 menu and the classic full context menu.",
            @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "",
            [new("Windows 11 menu", 0, DeleteRegistryValue: true), new("Classic full menu", 1, RegistryString: "")], 0, true),
        new("explorer-app-mode", "Explorer", "App color mode", "Set light or dark colors for File Explorer and other Windows apps.", Personalize, "AppsUseLightTheme",
            [new("Light", 1), new("Dark", 0)], 1, true),
        new("explorer-system-mode", "Explorer", "System color mode", "Set light or dark colors for Windows shell surfaces such as the taskbar and Start menu.", Personalize, "SystemUsesLightTheme",
            [new("Light", 1), new("Dark", 0)], 1, true),
        new("explorer-transparency", "Explorer", "Transparency effects", "Enable or disable Windows transparency effects across supported shell and app surfaces.", Personalize, "EnableTransparency",
            [new("On", 1), new("Off", 0)], 1, true),
        new("explorer-scrollbars", "Explorer", "Scrollbars", "Keep scrollbars visible in Windows and classic Win32 app surfaces.", Accessibility, "DynamicScrollbars",
            [new("Always show", 1), new("Auto-hide", 0)], 0, true),
        new("taskbar-tray-icons", "Taskbar", "Notification area icons", "Choose whether Windows keeps notification-area icons visible or collapses them behind the tray overflow.", ExplorerRoot, "EnableAutoTray",
            [new("Show all icons", 0), new("Collapse inactive icons", 1)], 1, true),
        new("taskbar-size", "Taskbar", "Taskbar size", "Choose the compact, default, or larger Windows taskbar height and button scale.", ExplorerAdvanced, "TaskbarSi",
            [new("Small", 0), new("Medium", 1), new("Large", 2)], 1, true),
        new("taskbar-clock-seconds", "Taskbar", "Clock seconds", "Choose whether the Windows taskbar clock includes seconds.", ExplorerAdvanced, "ShowSecondsInSystemClock",
            [new("Hide seconds", 0), new("Show seconds", 1)], 0, true),
        new("taskbar-show-desktop", "Taskbar", "Show desktop button", "Choose whether Windows keeps the narrow Show desktop target at the end of the taskbar.", ExplorerAdvanced, "TaskbarSd",
            [new("Show", 1), new("Hide", 0)], 1, true),
        new("taskbar-task-view", "Taskbar", "Task View button", "Choose whether Windows shows the Task View button on the taskbar.", ExplorerAdvanced, "TaskbarMn",
            [new("Show", 1), new("Hide", 0)], 1, true),
        new("taskbar-widgets", "Taskbar", "Widgets button", "Choose whether Windows shows the Widgets button on the taskbar.", ExplorerAdvanced, "TaskbarDa",
            [new("Show", 1), new("Hide", 0)], 1, true)
    ];

    public static SettingDefinition ById(string id) => All.Single(setting => setting.Id == id);
}
