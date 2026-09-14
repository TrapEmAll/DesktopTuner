namespace DesktopTuner;

public static class DesktopHostDisplayLayoutPolicy
{
    private const double DefaultContentInset = 12;

    public static TaskbarBounds CalculateVirtualBounds(IEnumerable<TaskbarDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var connectedDisplays = displays.ToArray();
        if (connectedDisplays.Length == 0)
            throw new ArgumentException("At least one display is required.", nameof(displays));
        if (connectedDisplays.Any(display => display.Width <= 0 || display.Height <= 0))
            throw new ArgumentException("Every display must have positive bounds.", nameof(displays));

        var left = connectedDisplays.Min(display => display.Left);
        var top = connectedDisplays.Min(display => display.Top);
        var right = connectedDisplays.Max(display => (long)display.Left + display.Width);
        var bottom = connectedDisplays.Max(display => (long)display.Top + display.Height);
        return new TaskbarBounds(left, top, right - left, bottom - top);
    }

    public static IReadOnlyList<DesktopHostMonitorViewport> CreateMonitorViewports(
        IEnumerable<TaskbarDisplay> displays,
        TaskbarBounds virtualBounds,
        double canvasWidth,
        double canvasHeight,
        double scaleX,
        double scaleY,
        double contentInset = DefaultContentInset)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (!double.IsFinite(canvasWidth) || canvasWidth <= 0 || !double.IsFinite(canvasHeight) || canvasHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(canvasWidth), "Desktop canvas dimensions must be positive and finite.");
        if (!double.IsFinite(scaleX) || scaleX <= 0 || !double.IsFinite(scaleY) || scaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(scaleX), "Display scales must be positive and finite.");
        if (!double.IsFinite(contentInset) || contentInset < 0)
            throw new ArgumentOutOfRangeException(nameof(contentInset));

        var viewports = new List<DesktopHostMonitorViewport>();
        foreach (var display in displays)
        {
            var left = Math.Clamp((display.Left - virtualBounds.Left) / scaleX - contentInset, 0, canvasWidth);
            var top = Math.Clamp((display.Top - virtualBounds.Top) / scaleY - contentInset, 0, canvasHeight);
            var right = Math.Clamp(((long)display.Left + display.Width - virtualBounds.Left) / scaleX - contentInset, 0, canvasWidth);
            var bottom = Math.Clamp(((long)display.Top + display.Height - virtualBounds.Top) / scaleY - contentInset, 0, canvasHeight);
            if (right <= left || bottom <= top) continue;
            viewports.Add(new DesktopHostMonitorViewport(display.DeviceName, left, top, right - left, bottom - top, display.IsPrimary));
        }
        if (viewports.Count == 0)
            throw new ArgumentException("Connected displays do not overlap the desktop canvas.", nameof(displays));
        return viewports;
    }

    public static DesktopHostMonitorViewport FindNearestMonitor(
        IReadOnlyList<DesktopHostMonitorViewport> monitors,
        DesktopHostPosition point)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0) throw new ArgumentException("At least one desktop monitor is required.", nameof(monitors));
        return monitors
            .OrderBy(monitor => DistanceSquared(point, monitor))
            .ThenByDescending(monitor => monitor.IsPrimary)
            .First();
    }

    private static double DistanceSquared(DesktopHostPosition point, DesktopHostMonitorViewport monitor)
    {
        var x = Math.Clamp(point.Left, monitor.Left, monitor.Left + monitor.Width);
        var y = Math.Clamp(point.Top, monitor.Top, monitor.Top + monitor.Height);
        return Math.Pow(point.Left - x, 2) + Math.Pow(point.Top - y, 2);
    }
}
