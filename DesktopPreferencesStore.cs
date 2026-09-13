using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public enum TaskbarEdge { Bottom, Top, Left, Right }
public enum TaskbarSize { Small, Standard, Large }
public sealed record PinnedTaskbarApp(string Name, string ExecutablePath);
public sealed record DesktopPreferences(TaskbarEdge TaskbarEdge, TaskbarSize TaskbarSize = TaskbarSize.Standard, bool AutoHide = false, List<PinnedTaskbarApp>? PinnedApps = null);

public sealed class DesktopPreferencesStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner", "preferences.json");

    public DesktopPreferences Load()
    {
        if (!File.Exists(_path)) return new DesktopPreferences(TaskbarEdge.Bottom);
        try
        {
            var value = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(_path));
            if (value is null || !Enum.IsDefined(value.TaskbarEdge) || !Enum.IsDefined(value.TaskbarSize))
                return new DesktopPreferences(TaskbarEdge.Bottom);
            var pins = (value.PinnedApps ?? [])
                .Where(app => app is not null && !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.ExecutablePath) && Path.IsPathFullyQualified(app.ExecutablePath) &&
                    string.Equals(Path.GetExtension(app.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
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
