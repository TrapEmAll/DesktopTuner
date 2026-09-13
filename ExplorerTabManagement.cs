namespace DesktopTuner;

public static class ExplorerTabManagement
{
    public static IReadOnlyList<ExplorerTabState> GetTabsToClose(
        IReadOnlyList<ExplorerTabState> tabs,
        ExplorerTabState anchor,
        bool closeOtherTabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(anchor);
        var anchorIndex = IndexOfReference(tabs, anchor);
        if (anchorIndex < 0) return [];

        return closeOtherTabs
            ? tabs.Where(tab => !ReferenceEquals(tab, anchor)).ToArray()
            : tabs.Skip(anchorIndex + 1).ToArray();
    }

    private static int IndexOfReference(IReadOnlyList<ExplorerTabState> tabs, ExplorerTabState target)
    {
        for (var index = 0; index < tabs.Count; index++)
            if (ReferenceEquals(tabs[index], target)) return index;
        return -1;
    }
}
