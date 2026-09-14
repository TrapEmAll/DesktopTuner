namespace DesktopTuner;

public sealed record ExplorerLocation(string? Path, bool IsDriveList = false, string? SearchQuery = null, bool IsHome = false, bool IsRecycleBin = false);

public sealed class ExplorerTabState(ExplorerLocation location)
{
    public ExplorerLocation Location { get; set; } = location;
    public List<ExplorerLocation> Back { get; } = [];
    public List<ExplorerLocation> Forward { get; } = [];
    public IReadOnlyList<ExplorerEntry> Entries { get; set; } = [];
    public CancellationTokenSource? SearchCancellation { get; set; }
    public bool IsSearchView { get; set; }
    public ExplorerViewMode ViewMode { get; set; } = ExplorerViewMode.Details;
    public ExplorerSortColumn SortColumn { get; set; } = ExplorerSortColumn.Name;
    public bool SortAscending { get; set; } = true;
    public ExplorerSortColumn HomeSortColumn { get; set; } = ExplorerSortColumn.Name;
    public bool HomeSortAscending { get; set; } = true;
    public bool HomeSortExplicitly { get; set; }
    public bool GroupDrives { get; set; } = true;

    public ExplorerTabState Duplicate()
    {
        var duplicate = new ExplorerTabState(Location)
        {
            ViewMode = ViewMode,
            SortColumn = SortColumn,
            SortAscending = SortAscending,
            HomeSortColumn = HomeSortColumn,
            HomeSortAscending = HomeSortAscending,
            HomeSortExplicitly = HomeSortExplicitly,
            GroupDrives = GroupDrives,
            IsSearchView = IsSearchView
        };
        duplicate.Back.AddRange(Back);
        duplicate.Forward.AddRange(Forward);
        return duplicate;
    }

    public void PushHistory(ExplorerLocation current)
    {
        Back.Add(current);
        Forward.Clear();
    }

    public ExplorerLocation? GoBack(ExplorerLocation current)
    {
        if (Back.Count == 0) return null;
        Forward.Add(current);
        var target = Back[^1];
        Back.RemoveAt(Back.Count - 1);
        return target;
    }

    public ExplorerLocation? GoForward(ExplorerLocation current)
    {
        if (Forward.Count == 0) return null;
        Back.Add(current);
        var target = Forward[^1];
        Forward.RemoveAt(Forward.Count - 1);
        return target;
    }
}
