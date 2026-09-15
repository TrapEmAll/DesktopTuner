namespace DesktopTuner;

public static class TaskbarWindowWheelPolicy
{
    public static RunningWindow? SelectTarget(IEnumerable<RunningWindow> windows, int delta)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (delta == 0) return null;

        var ordered = windows.ToArray();
        if (ordered.Length < 2) return null;

        var currentIndex = Array.FindIndex(ordered, window => window.IsForeground);
        var forward = delta < 0;
        var origin = currentIndex < 0 ? (forward ? -1 : 0) : currentIndex;
        var targetIndex = (origin + (forward ? 1 : -1) + ordered.Length) % ordered.Length;
        return ordered[targetIndex];
    }
}
