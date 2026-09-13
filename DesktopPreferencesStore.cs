using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public enum TaskbarEdge { Bottom, Top, Left, Right }
public enum TaskbarSize { Small, Standard, Large }
public sealed record DesktopPreferences(TaskbarEdge TaskbarEdge, TaskbarSize TaskbarSize = TaskbarSize.Standard, bool AutoHide = false);

public sealed class DesktopPreferencesStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner", "preferences.json");

    public DesktopPreferences Load()
    {
        if (!File.Exists(_path)) return new DesktopPreferences(TaskbarEdge.Bottom);
        try
        {
            var value = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(_path));
            return value is not null && Enum.IsDefined(value.TaskbarEdge) && Enum.IsDefined(value.TaskbarSize)
                ? value
                : new DesktopPreferences(TaskbarEdge.Bottom);
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
