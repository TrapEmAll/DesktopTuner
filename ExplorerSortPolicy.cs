namespace DesktopTuner;

public enum ExplorerSortColumn
{
    Name,
    DateModified,
    Type,
    Size
}

public static class ExplorerSortPolicy
{
    public static IReadOnlyList<ExplorerEntry> Sort(
        IEnumerable<ExplorerEntry> entries,
        ExplorerSortColumn column,
        bool ascending)
    {
        var groups = entries.GroupBy(entry => entry.IsDirectory)
            .OrderByDescending(group => group.Key);
        var result = new List<ExplorerEntry>();
        foreach (var group in groups)
        {
            var ordered = column switch
            {
                ExplorerSortColumn.Name => ascending
                    ? group.OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    : group.OrderByDescending(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                ExplorerSortColumn.DateModified => ascending
                    ? group.OrderBy(entry => entry.Modified)
                    : group.OrderByDescending(entry => entry.Modified),
                ExplorerSortColumn.Type => ascending
                    ? group.OrderBy(entry => entry.Type, StringComparer.CurrentCultureIgnoreCase)
                    : group.OrderByDescending(entry => entry.Type, StringComparer.CurrentCultureIgnoreCase),
                ExplorerSortColumn.Size => ascending
                    ? group.OrderBy(entry => entry.Length)
                    : group.OrderByDescending(entry => entry.Length),
                _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Unknown Explorer sort column.")
            };
            result.AddRange(ordered.ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase));
        }
        return result;
    }
}
