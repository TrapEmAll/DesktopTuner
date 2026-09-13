using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public sealed record ExplorerTabSession(
    ExplorerLocation Location,
    List<ExplorerLocation>? Back = null,
    List<ExplorerLocation>? Forward = null,
    ExplorerViewMode ViewMode = ExplorerViewMode.Details,
    ExplorerSortColumn SortColumn = ExplorerSortColumn.Name,
    bool SortAscending = true,
    ExplorerSortColumn HomeSortColumn = ExplorerSortColumn.Name,
    bool HomeSortAscending = true,
    bool HomeSortExplicitly = false,
    bool GroupDrives = true);

public sealed record ExplorerSession(int ActiveTabIndex, List<ExplorerTabSession> Tabs);

public sealed class ExplorerSessionStore
{
    public const int MaximumTabs = 20;
    public const int MaximumHistoryEntries = 50;
    private readonly string _path;

    public ExplorerSessionStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTuner", "explorer-session.json"));
    }

    public ExplorerSession? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var session = JsonSerializer.Deserialize<ExplorerSession>(File.ReadAllText(_path));
            if (session?.Tabs is not { Count: > 0 }) return null;
            var tabs = session.Tabs
                .Where(tab => tab is not null && tab.Location is not null
                    && Enum.IsDefined(tab.ViewMode)
                    && Enum.IsDefined(tab.SortColumn)
                    && Enum.IsDefined(tab.HomeSortColumn))
                .Take(MaximumTabs)
                .Select(tab => tab with
                {
                    Back = (tab.Back ?? []).Where(location => location is not null).TakeLast(MaximumHistoryEntries).ToList(),
                    Forward = (tab.Forward ?? []).Where(location => location is not null).TakeLast(MaximumHistoryEntries).ToList()
                })
                .ToList();
            if (tabs.Count == 0) return null;
            return new ExplorerSession(Math.Clamp(session.ActiveTabIndex, 0, tabs.Count - 1), tabs);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Save(ExplorerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var tabs = session.Tabs
            .Where(tab => tab is not null && tab.Location is not null)
            .Take(MaximumTabs)
            .Select(tab => tab with
            {
                Back = (tab.Back ?? []).TakeLast(MaximumHistoryEntries).ToList(),
                Forward = (tab.Forward ?? []).TakeLast(MaximumHistoryEntries).ToList()
            })
            .ToList();
        if (tabs.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new ExplorerSession(Math.Clamp(session.ActiveTabIndex, 0, tabs.Count - 1), tabs), new JsonSerializerOptions { WriteIndented = true }));
    }
}
