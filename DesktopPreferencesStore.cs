using System.IO;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace DesktopTuner;

public enum TaskbarEdge { Bottom, Top, Left, Right }
public enum TaskbarSize { Small, Standard, Large }
public enum TaskbarStyle { EdgeToEdge, Floating, Segmented, DockLike }
public enum TaskbarGroupingMode { Always, WhenFull, Never }
public enum TaskbarWindowDisplayMode { AllTaskbars, TaskbarOnWhichWindowIsOpen, PrimaryAndTaskbarOnWhichWindowIsOpen }
public enum TaskbarButtonAlignment { Left, Center }
public enum TaskbarIconSize { Small, Standard, Large }
public enum TaskbarButtonSpacing { Compact, Standard, Relaxed }
public enum TaskbarLabelVisibility { Always, WhenFull, Never }
public enum TaskbarButtonEffect { Accent, Aura, DynamicAura }
public enum TaskbarVisualStyle { Windows11, Windows10, Windows7 }
public enum StartMenuStyle { Modern, Classic, Compact, Windows7, Windows8, Windows10 }
public enum StartTileSize { Small, Medium, Wide, Large }
public sealed record PinnedTaskbarApp(string Name, string ExecutablePath, bool IsDirectory = false, bool IsPackagedApp = false, bool IsShellNamespace = false, List<TaskbarJumpListDestination>? PinnedDestinations = null)
{
    [JsonIgnore]
    public bool CanOpenLocation => TaskbarPinCatalog.CanOpenLocation(this);
    [JsonIgnore]
    public bool CanRunElevated => TaskbarPinCatalog.CanRunAsAdministrator(Name, ExecutablePath, IsDirectory, IsShellNamespace);
    [JsonIgnore]
    public bool CanShowJumpList => !IsDirectory && !IsShellNamespace && (IsPackagedApp || File.Exists(ExecutablePath));
    [JsonIgnore]
    public bool CanPinToStart => !IsShellNamespace || TaskbarPinCatalog.IsSupportedShellNamespaceTarget(ExecutablePath);
    [JsonIgnore]
    public bool HasPinnedDestinations => PinnedDestinations is { Count: > 0 };
}
public sealed record TaskbarWeatherSettings(bool Enabled = false, string LocationQuery = "", string LocationName = "", double? Latitude = null, double? Longitude = null);
public sealed record DesktopPreferences(TaskbarEdge TaskbarEdge, TaskbarSize TaskbarSize = TaskbarSize.Standard, bool AutoHide = false, List<PinnedTaskbarApp>? PinnedApps = null, bool ReplaceWindowsKey = false, StartMenuStyle StartMenuStyle = StartMenuStyle.Modern, bool TaskbarOnAllDisplays = false, TaskbarStyle TaskbarLayout = TaskbarStyle.EdgeToEdge, TaskbarGroupingMode TaskbarGrouping = TaskbarGroupingMode.Always, TaskbarButtonAlignment TaskbarButtonAlignment = TaskbarButtonAlignment.Center, bool TaskbarShowLabels = true, TaskbarIconSize TaskbarIconSize = TaskbarIconSize.Standard, TaskbarButtonSpacing TaskbarButtonSpacing = TaskbarButtonSpacing.Standard, bool StartWithWindows = false, bool AutoHideWhenMaximized = false, int TaskbarTransparency = 5, List<AppEntry>? PinnedStartApps = null, bool ReplaceNativeTaskbar = false, bool TaskbarDynamicTransparency = false, TaskbarButtonEffect TaskbarButtonEffect = TaskbarButtonEffect.Accent, StartMenuPlacePreferences? StartMenuPlaces = null, int StartRecentAppCount = 4, TaskbarSystemButtonVisibility? TaskbarSystemButtons = null, bool CenterStartMenu = false, TaskbarWindowDisplayMode TaskbarWindowDisplayMode = TaskbarWindowDisplayMode.AllTaskbars, bool FolderShellIntegrationEnabled = false, bool TaskbarShowWindowsFromAllVirtualDesktops = false, bool ReplaceExplorerShortcut = false, TaskbarVisualStyle TaskbarVisualStyle = TaskbarVisualStyle.Windows11, ControlPanelAppletPreferences? ControlPanelApplets = null, TaskbarWeatherSettings? TaskbarWeather = null, bool TaskbarLocked = false, TaskbarLabelVisibility TaskbarLabelVisibility = TaskbarLabelVisibility.Always);

public sealed class DesktopPreferencesStore
{
    private readonly string _path;

    public DesktopPreferencesStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner", "preferences.json");
    }

    public DesktopPreferences Load()
    {
        if (!File.Exists(_path)) return new DesktopPreferences(TaskbarEdge.Bottom, TaskbarOnAllDisplays: true, TaskbarWeather: new TaskbarWeatherSettings());
        try
        {
            var value = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(_path));
            if (value is null || !Enum.IsDefined(value.TaskbarEdge) || !Enum.IsDefined(value.TaskbarSize) || !Enum.IsDefined(value.StartMenuStyle) || !Enum.IsDefined(value.TaskbarLayout) || !Enum.IsDefined(value.TaskbarGrouping) || !Enum.IsDefined(value.TaskbarButtonAlignment) || !Enum.IsDefined(value.TaskbarIconSize) || !Enum.IsDefined(value.TaskbarButtonSpacing) || !Enum.IsDefined(value.TaskbarButtonEffect) || !Enum.IsDefined(value.TaskbarWindowDisplayMode) || !Enum.IsDefined(value.TaskbarVisualStyle) || !Enum.IsDefined(value.TaskbarLabelVisibility))
                return new DesktopPreferences(TaskbarEdge.Bottom);
            var pins = (value.PinnedApps ?? [])
                .Where(app => app is not null && !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.ExecutablePath) &&
                    (app.IsShellNamespace && TaskbarPinCatalog.IsSupportedShellNamespaceTarget(app.ExecutablePath) ||
                        TaskbarPinCatalog.IsSupportedTarget(app.ExecutablePath, app.IsDirectory) ||
                        app.IsPackagedApp && TaskbarPinCatalog.IsSupportedPackagedTarget(app.ExecutablePath)))
                .DistinctBy(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Take(TaskbarPinCatalog.MaximumPins)
                .Select(app => app with { PinnedDestinations = TaskbarJumpListPolicy.NormalizeDestinations(app.PinnedDestinations ?? []).ToList() })
                .ToList();
            var startPins = StartPinCatalog.Normalize(value.PinnedStartApps).ToList();
            var startPlaces = StartMenuPlaceCatalog.Normalize(value.StartMenuPlaces);
            var controlPanelApplets = ControlPanelAppletCatalog.Normalize(value.ControlPanelApplets);
            var labels = !value.TaskbarShowLabels && value.TaskbarLabelVisibility == TaskbarLabelVisibility.Always ? TaskbarLabelVisibility.Never : value.TaskbarLabelVisibility;
            return value with { PinnedApps = pins, PinnedStartApps = startPins, StartMenuPlaces = startPlaces, ControlPanelApplets = controlPanelApplets, TaskbarWeather = TaskbarWeatherPolicy.Normalize(value.TaskbarWeather), TaskbarTransparency = TaskbarTransparencyPolicy.Clamp(value.TaskbarTransparency), StartRecentAppCount = Math.Clamp(value.StartRecentAppCount, 0, StartRecentAppsStore.MaximumEntries), TaskbarSystemButtons = TaskbarSystemButtonVisibility.Normalize(value.TaskbarSystemButtons), TaskbarLabelVisibility = labels };
        }
        catch (JsonException) { return new DesktopPreferences(TaskbarEdge.Bottom); }
        catch (IOException) { return new DesktopPreferences(TaskbarEdge.Bottom); }
    }

    public void Save(DesktopPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
    }
}
