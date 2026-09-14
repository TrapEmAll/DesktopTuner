namespace DesktopTuner;

internal sealed class SharedSnapshotCache<T>(TimeSpan maximumAge, Func<long>? getTimestamp = null)
{
    private readonly object _gate = new();
    private readonly long _maximumAgeMilliseconds = ValidateMaximumAge(maximumAge);
    private readonly Func<long> _getTimestamp = getTimestamp ?? (() => Environment.TickCount64);
    private T[]? _snapshot;
    private long _snapshotTimestamp;

    public IReadOnlyList<T> GetOrRefresh(Func<IReadOnlyList<T>> refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        lock (_gate)
        {
            var now = _getTimestamp();
            var age = now - _snapshotTimestamp;
            if (_snapshot is not null && age >= 0 && age < _maximumAgeMilliseconds) return _snapshot;

            _snapshot = refresh().ToArray();
            _snapshotTimestamp = _getTimestamp();
            return _snapshot;
        }
    }

    private static long ValidateMaximumAge(TimeSpan maximumAge)
    {
        if (!double.IsFinite(maximumAge.TotalMilliseconds) || maximumAge.TotalMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumAge), "A shared snapshot must have a positive, finite lifetime.");
        return (long)Math.Ceiling(maximumAge.TotalMilliseconds);
    }
}
