namespace DesktopTuner;

public sealed record ShellHostPowerMenuCommand(string Id, string Label, string? Target = null);

public static class ShellHostPowerMenuCatalog
{
    public static IReadOnlyList<ShellHostPowerMenuCommand> SystemCommands { get; } =
    [
        new("apps", "Installed apps", "ms-settings:appsfeatures"),
        new("power-options", "Power Options", "powercfg.cpl"),
        new("event-viewer", "Event Viewer", "eventvwr.msc"),
        new("system", "System", "ms-settings:about"),
        new("device-manager", "Device Manager", "devmgmt.msc"),
        new("network-connections", "Network Connections", "ncpa.cpl"),
        new("disk-management", "Disk Management", "diskmgmt.msc"),
        new("computer-management", "Computer Management", "compmgmt.msc"),
        new("terminal", "Terminal", "wt.exe"),
        new("task-manager", "Task Manager", "taskmgr.exe"),
        new("settings", "Settings", "ms-settings:")
    ];

    public static IReadOnlyList<StartPowerAction> PowerActions { get; } =
    [
        StartPowerActionCatalog.ById("lock"),
        StartPowerActionCatalog.ById("sleep"),
        StartPowerActionCatalog.ById("hibernate"),
        StartPowerActionCatalog.ById("sign-out"),
        StartPowerActionCatalog.ById("shutdown"),
        StartPowerActionCatalog.ById("restart")
    ];

    public static ShellHostPowerMenuCommand SystemCommand(string id) =>
        SystemCommands.SingleOrDefault(command => string.Equals(command.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown shell-host system command.");
}
