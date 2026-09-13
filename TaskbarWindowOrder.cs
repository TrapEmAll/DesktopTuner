namespace DesktopTuner;

public sealed class TaskbarWindowOrder
{
    private readonly List<nint> _handles = [];

    public IReadOnlyList<RunningWindow> Synchronize(IEnumerable<RunningWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var current = windows.ToList();
        var currentHandles = current.Select(window => window.Handle).ToHashSet();
        _handles.RemoveAll(handle => !currentHandles.Contains(handle));
        var knownHandles = _handles.ToHashSet();
        foreach (var window in current)
            if (knownHandles.Add(window.Handle)) _handles.Add(window.Handle);

        var positions = _handles.Select((handle, index) => (handle, index)).ToDictionary(item => item.handle, item => item.index);
        return current.OrderBy(window => positions[window.Handle]).ToList();
    }

    public bool MoveGroup(IEnumerable<RunningWindow> windows, TaskbarWindowGroup moving, TaskbarWindowGroup target)
    {
        ArgumentNullException.ThrowIfNull(moving);
        ArgumentNullException.ThrowIfNull(target);
        Synchronize(windows);

        var movingHandles = moving.Windows.Select(window => window.Handle).ToHashSet();
        var targetHandles = target.Windows.Select(window => window.Handle).ToHashSet();
        if (movingHandles.Count == 0 || targetHandles.Count == 0 || movingHandles.Overlaps(targetHandles)) return false;
        if (!_handles.Any(targetHandles.Contains)) return false;

        var orderedMoving = _handles.Where(movingHandles.Contains).ToList();
        if (orderedMoving.Count == 0) return false;
        _handles.RemoveAll(movingHandles.Contains);
        var insertionIndex = _handles.FindIndex(targetHandles.Contains);
        if (insertionIndex < 0) return false;
        _handles.InsertRange(insertionIndex, orderedMoving);
        return true;
    }

    public bool MoveWindow(IEnumerable<RunningWindow> windows, RunningWindow moving, RunningWindow target)
    {
        ArgumentNullException.ThrowIfNull(moving);
        ArgumentNullException.ThrowIfNull(target);
        if (moving.Handle == target.Handle) return false;

        var orderedHandles = Synchronize(windows).Select(window => window.Handle).ToList();
        var movingIndex = orderedHandles.IndexOf(moving.Handle);
        if (movingIndex < 0 || !orderedHandles.Contains(target.Handle)) return false;

        orderedHandles.RemoveAt(movingIndex);
        var targetIndex = orderedHandles.IndexOf(target.Handle);
        if (targetIndex < 0) return false;
        orderedHandles.Insert(targetIndex, moving.Handle);
        _handles.Clear();
        _handles.AddRange(orderedHandles);
        return true;
    }
}
