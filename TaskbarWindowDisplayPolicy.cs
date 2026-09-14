namespace DesktopTuner;

public static class TaskbarWindowDisplayPolicy
{
    public static IReadOnlyList<RunningWindow> Filter(
        IEnumerable<RunningWindow> windows,
        TaskbarDisplay taskbarDisplay,
        IEnumerable<TaskbarDisplay> connectedDisplays,
        TaskbarWindowDisplayMode mode)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(taskbarDisplay);
        ArgumentNullException.ThrowIfNull(connectedDisplays);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));

        var entries = windows.ToList();
        if (mode == TaskbarWindowDisplayMode.AllTaskbars) return entries;

        var displays = connectedDisplays
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(connectedDisplays));

        var primary = displays.FirstOrDefault(display => display.IsPrimary) ?? displays[0];
        return entries.Where(window =>
        {
            var windowDisplay = FindWindowDisplay(window.Bounds, displays);
            return SameDisplay(windowDisplay, taskbarDisplay)
                || mode == TaskbarWindowDisplayMode.PrimaryAndTaskbarOnWhichWindowIsOpen && SameDisplay(primary, taskbarDisplay);
        }).ToList();
    }

    private static TaskbarDisplay FindWindowDisplay(TaskbarBounds bounds, IReadOnlyList<TaskbarDisplay> displays)
    {
        var centerX = bounds.Left + bounds.Width / 2d;
        var centerY = bounds.Top + bounds.Height / 2d;
        var containing = displays.FirstOrDefault(display =>
            centerX >= display.Left && centerX < display.Left + display.Width &&
            centerY >= display.Top && centerY < display.Top + display.Height);
        if (containing is not null) return containing;

        return displays
            .Select(display => (Display: display, Area: IntersectionArea(bounds, display)))
            .OrderByDescending(item => item.Area)
            .ThenByDescending(item => item.Display.IsPrimary)
            .First().Display;
    }

    private static double IntersectionArea(TaskbarBounds bounds, TaskbarDisplay display)
    {
        var width = Math.Max(0, Math.Min(bounds.Left + bounds.Width, display.Left + display.Width) - Math.Max(bounds.Left, display.Left));
        var height = Math.Max(0, Math.Min(bounds.Top + bounds.Height, display.Top + display.Height) - Math.Max(bounds.Top, display.Top));
        return width * height;
    }

    private static bool SameDisplay(TaskbarDisplay left, TaskbarDisplay right) =>
        string.Equals(left.DeviceName, right.DeviceName, StringComparison.OrdinalIgnoreCase);
}
