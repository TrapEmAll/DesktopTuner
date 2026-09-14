namespace DesktopTuner;

public static class ExplorerTabOrdering
{
    public static bool Transfer<T>(IList<T> source, IList<T> destination, int sourceIndex, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(source, destination)) return Move(source, sourceIndex, insertionIndex);
        if (sourceIndex < 0 || sourceIndex >= source.Count || insertionIndex < 0 || insertionIndex > destination.Count) return false;

        var tab = source[sourceIndex];
        if (destination.Contains(tab)) return false;
        source.RemoveAt(sourceIndex);
        destination.Insert(insertionIndex, tab);
        return true;
    }

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
