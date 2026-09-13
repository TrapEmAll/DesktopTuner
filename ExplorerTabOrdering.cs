namespace DesktopTuner;

public static class ExplorerTabOrdering
{
    public static bool Move<T>(IList<T> tabs, int sourceIndex, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (sourceIndex < 0 || sourceIndex >= tabs.Count) return false;
        if (insertionIndex < 0 || insertionIndex > tabs.Count) return false;

        var destinationIndex = insertionIndex > sourceIndex ? insertionIndex - 1 : insertionIndex;
        if (destinationIndex == sourceIndex) return false;

        var tab = tabs[sourceIndex];
        tabs.RemoveAt(sourceIndex);
        tabs.Insert(destinationIndex, tab);
        return true;
    }
}
