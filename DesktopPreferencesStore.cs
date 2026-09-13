using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public enum TaskbarEdge { Bottom, Top, Left, Right }
public enum TaskbarSize { Small, Standard, Large }
public enum TaskbarStyle { EdgeToEdge, Floating, Segmented }
public enum StartMenuStyle { Modern, Classic, Compact }
public sealed record PinnedTaskbarApp(string Name, string ExecutablePath, bool IsDirectory = false);
public sealed record DesktopPreferences(TaskbarEdge TaskbarEdge, TaskbarSize TaskbarSize = TaskbarSize.Standard, bool AutoHide = false, List<PinnedTaskbarApp>? PinnedApps = null, bool ReplaceWindowsKey = false, StartMenuStyle StartMenuStyle = StartMenuStyle.Modern, bool TaskbarOnAllDisplays = false, TaskbarStyle TaskbarLayout = TaskbarStyle.EdgeToEdge);

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
            if (value is null || !Enum.IsDefined(value.TaskbarEdge) || !Enum.IsDefined(value.TaskbarSize) || !Enum.IsDefined(value.StartMenuStyle) || !Enum.IsDefined(value.TaskbarLayout))
                return new DesktopPreferences(TaskbarEdge.Bottom);
            var pins = (value.PinnedApps ?? [])
                .Where(app => app is not null && !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.ExecutablePath) &&
                    TaskbarPinCatalog.IsSupportedTarget(app.ExecutablePath, app.IsDirectory))
                .DistinctBy(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Take(TaskbarPinCatalog.MaximumPins)
                .ToList();
            return value with { PinnedApps = pins };
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
