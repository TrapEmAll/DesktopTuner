namespace DesktopTuner;

public sealed record SettingChoice(string Label, int Value);

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
        new("explorer-extensions", "Explorer", "File name extensions", "Show or hide extensions such as .txt and .png.", ExplorerAdvanced, "HideFileExt",
            [new("Show extensions", 0), new("Hide extensions", 1)], 1),
        new("explorer-hidden", "Explorer", "Hidden files", "Show or hide files marked as hidden.", ExplorerAdvanced, "Hidden",
            [new("Show hidden files", 1), new("Hide hidden files", 2)], 2),
        new("explorer-compact", "Explorer", "Item spacing", "Use compact spacing in File Explorer lists.", ExplorerAdvanced, "UseCompactMode",
            [new("Compact", 1), new("Comfortable", 0)], 0)
    ];

    public static SettingDefinition ById(string id) => All.Single(setting => setting.Id == id);
}
