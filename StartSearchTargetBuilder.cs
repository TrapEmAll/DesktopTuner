namespace DesktopTuner;

public static class StartSearchTargetBuilder
{
    public static string WindowsSearch(string query) =>
        $"search:query={Uri.EscapeDataString(RequireQuery(query))}";

    public static string WebSearch(string query) =>
        $"https://www.bing.com/search?q={Uri.EscapeDataString(RequireQuery(query))}";

    private static string RequireQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var normalized = query.Trim();
        if (normalized.Length == 0) throw new ArgumentException("Enter a search query first.", nameof(query));
        return normalized;
    }
}
