namespace DesktopTuner;

public static class ExplorerClosedTabHistory
{
    public const int MaximumClosedTabs = 10;

    public static void Remember(IList<ExplorerTabState> history, ExplorerTabState tab, int maximum = MaximumClosedTabs)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tab);
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (maximum == 0) return;

        history.Remove(tab);
        history.Add(tab);
        while (history.Count > maximum) history.RemoveAt(0);
    }

    public static ExplorerTabState? RestoreLast(IList<ExplorerTabState> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count == 0) return null;

        var index = history.Count - 1;
        var tab = history[index];
        history.RemoveAt(index);
        return tab;
    }
}
