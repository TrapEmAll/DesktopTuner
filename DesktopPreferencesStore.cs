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
public enum TaskbarButtonEffect { Accent, Aura, DynamicAura }
public enum StartMenuStyle { Modern, Classic, Compact, Windows7, Windows8, Windows10 }
public enum StartTileSize { Small, Medium, Wide, Large }
public sealed record PinnedTaskbarApp(string Name, string ExecutablePath, bool IsDirectory = false)
{
    [JsonIgnore]
    public bool CanOpenLocation => TaskbarPinCatalog.CanOpenLocation(this);
}
public sealed record DesktopPreferences(TaskbarEdge TaskbarEdge, TaskbarSize TaskbarSize = TaskbarSize.Standard, bool AutoHide = false, List<PinnedTaskbarApp>? PinnedApps = null, bool ReplaceWindowsKey = false, StartMenuStyle StartMenuStyle = StartMenuStyle.Modern, bool TaskbarOnAllDisplays = false, TaskbarStyle TaskbarLayout = TaskbarStyle.EdgeToEdge, TaskbarGroupingMode TaskbarGrouping = TaskbarGroupingMode.Always, TaskbarButtonAlignment TaskbarButtonAlignment = TaskbarButtonAlignment.Center, bool TaskbarShowLabels = true, TaskbarIconSize TaskbarIconSize = TaskbarIconSize.Standard, TaskbarButtonSpacing TaskbarButtonSpacing = TaskbarButtonSpacing.Standard, bool StartWithWindows = false, bool AutoHideWhenMaximized = false, int TaskbarTransparency = 5, List<AppEntry>? PinnedStartApps = null, bool ReplaceNativeTaskbar = false, bool TaskbarDynamicTransparency = false, TaskbarButtonEffect TaskbarButtonEffect = TaskbarButtonEffect.Accent, StartMenuPlacePreferences? StartMenuPlaces = null, int StartRecentAppCount = 4, TaskbarSystemButtonVisibility? TaskbarSystemButtons = null, bool CenterStartMenu = false, TaskbarWindowDisplayMode TaskbarWindowDisplayMode = TaskbarWindowDisplayMode.AllTaskbars, bool FolderShellIntegrationEnabled = false);

public sealed class DesktopPreferencesStore
{
    private readonly string _path;

    public DesktopPreferencesStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner", "preferences.json");
    }

    public DesktopPreferences Load()
    {
        if (!File.Exists(_path)) return new DesktopPreferences(TaskbarEdge.Bottom, TaskbarOnAllDisplays: true);
        try
        {
            var value = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(_path));
            if (value is null || !Enum.IsDefined(value.TaskbarEdge) || !Enum.IsDefined(value.TaskbarSize) || !Enum.IsDefined(value.StartMenuStyle) || !Enum.IsDefined(value.TaskbarLayout) || !Enum.IsDefined(value.TaskbarGrouping) || !Enum.IsDefined(value.TaskbarButtonAlignment) || !Enum.IsDefined(value.TaskbarIconSize) || !Enum.IsDefined(value.TaskbarButtonSpacing) || !Enum.IsDefined(value.TaskbarButtonEffect) || !Enum.IsDefined(value.TaskbarWindowDisplayMode))
                return new DesktopPreferences(TaskbarEdge.Bottom);
            var pins = (value.PinnedApps ?? [])
                .Where(app => app is not null && !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.ExecutablePath) &&
                    TaskbarPinCatalog.IsSupportedTarget(app.ExecutablePath, app.IsDirectory))
                .DistinctBy(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Take(TaskbarPinCatalog.MaximumPins)
                .ToList();
            var startPins = StartPinCatalog.Normalize(value.PinnedStartApps).ToList();
            var startPlaces = StartMenuPlaceCatalog.Normalize(value.StartMenuPlaces);
            return value with { PinnedApps = pins, PinnedStartApps = startPins, StartMenuPlaces = startPlaces, TaskbarTransparency = TaskbarTransparencyPolicy.Clamp(value.TaskbarTransparency), StartRecentAppCount = Math.Clamp(value.StartRecentAppCount, 0, StartRecentAppsStore.MaximumEntries), TaskbarSystemButtons = TaskbarSystemButtonVisibility.Normalize(value.TaskbarSystemButtons) };
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
