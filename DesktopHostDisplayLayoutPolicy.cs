namespace DesktopTuner;

public static class DesktopHostDisplayLayoutPolicy
{
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
}
