using System.Text.Json;
using System.IO;

namespace DesktopTuner;

public sealed record UserProfile(string Name, DateTimeOffset CreatedAt, Dictionary<string, int> Settings);
public sealed record RegistrySnapshot(string SettingId, bool Existed, string? Kind, string? Data);

public sealed class ProfileStore
{
    private readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void SaveUndo(IReadOnlyList<RegistrySnapshot> snapshots)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(UndoPath, JsonSerializer.Serialize(snapshots, JsonOptions));
    }

    public IReadOnlyList<RegistrySnapshot>? LoadUndo()
    {
        if (!File.Exists(UndoPath)) return null;
        return JsonSerializer.Deserialize<List<RegistrySnapshot>>(File.ReadAllText(UndoPath));
    }

    public void ClearUndo()
    {
        if (File.Exists(UndoPath)) File.Delete(UndoPath);
    }

    public void SaveProfile(string path, string name, IReadOnlyDictionary<string, int> settings)
    {
        var profile = new UserProfile(name, DateTimeOffset.Now, new Dictionary<string, int>(settings));
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));
    }

    public UserProfile LoadProfile(string path)
    {
        var profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The selected profile file is empty or invalid.");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Settings is null)
            throw new InvalidDataException("The selected file is not a Desktop Tuner profile.");
        return profile;
    }

    private string UndoPath => Path.Combine(_folder, "last-apply-undo.json");
}
