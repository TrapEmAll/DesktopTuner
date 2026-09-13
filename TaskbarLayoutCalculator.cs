namespace DesktopTuner;

public sealed record TaskbarBounds(double Left, double Top, double Width, double Height);

public static class TaskbarLayoutCalculator
{
    public static TaskbarBounds Calculate(TaskbarDisplay display, DesktopPreferences preferences, bool collapsed)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (display.ScaleX <= 0 || display.ScaleY <= 0) throw new ArgumentOutOfRangeException(nameof(display), "Display scale must be positive.");
        var local = Calculate(display.Width / display.ScaleX, display.Height / display.ScaleY, preferences, collapsed);
        return new TaskbarBounds(
            display.Left + local.Left * display.ScaleX,
            display.Top + local.Top * display.ScaleY,
            local.Width * display.ScaleX,
            local.Height * display.ScaleY);
    }

    public static TaskbarBounds CalculateStartMenu(TaskbarDisplay display, double menuWidth, double menuHeight, DesktopPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(preferences);
        if (menuWidth <= 0 || menuHeight <= 0) throw new ArgumentOutOfRangeException(nameof(menuWidth), "Menu dimensions must be positive.");
        if (display.ScaleX <= 0 || display.ScaleY <= 0) throw new ArgumentOutOfRangeException(nameof(display), "Display scale must be positive.");

        var taskbar = Calculate(display, preferences, collapsed: false);
        var width = menuWidth * display.ScaleX;
        var height = menuHeight * display.ScaleY;
        var gapX = 12 * display.ScaleX;
        var gapY = 12 * display.ScaleY;
        var left = preferences.TaskbarEdge switch
        {
            TaskbarEdge.Left => taskbar.Left + taskbar.Width + gapX,
            TaskbarEdge.Right => taskbar.Left - width - gapX,
            _ => display.Left + gapX
        };
        var top = preferences.TaskbarEdge switch
        {
            TaskbarEdge.Top => taskbar.Top + taskbar.Height + gapY,
            TaskbarEdge.Bottom => taskbar.Top - height - gapY,
            _ => display.Top + gapY
        };

        var minLeft = display.Left + 8 * display.ScaleX;
        var minTop = display.Top + 8 * display.ScaleY;
        var maxLeft = Math.Max(minLeft, display.Left + display.Width - width - 8 * display.ScaleX);
        var maxTop = Math.Max(minTop, display.Top + display.Height - height - 8 * display.ScaleY);
        left = Math.Clamp(left, minLeft, maxLeft);
        top = Math.Clamp(top, minTop, maxTop);
        return new TaskbarBounds(left, top, width, height);
    }

    public static TaskbarBounds Calculate(double screenWidth, double screenHeight, DesktopPreferences preferences, bool collapsed)
    {
        if (screenWidth <= 0 || screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth), "Screen dimensions must be positive.");
        ArgumentNullException.ThrowIfNull(preferences);

        var vertical = preferences.TaskbarEdge is TaskbarEdge.Left or TaskbarEdge.Right;
        if (vertical)
        {
            var width = collapsed ? 4 : preferences.TaskbarSize switch
            {
                TaskbarSize.Small => 150,
                TaskbarSize.Standard => 176,
                TaskbarSize.Large => 204,
                _ => throw new ArgumentOutOfRangeException(nameof(preferences), "Unknown taskbar size.")
            };
            return new TaskbarBounds(preferences.TaskbarEdge == TaskbarEdge.Left ? 0 : screenWidth - width, 0, width, screenHeight);
        }

        var height = collapsed ? 4 : preferences.TaskbarSize switch
        {
            TaskbarSize.Small => 46,
            TaskbarSize.Standard => 54,
            TaskbarSize.Large => 68,
            _ => throw new ArgumentOutOfRangeException(nameof(preferences), "Unknown taskbar size.")
        };
        return new TaskbarBounds(0, preferences.TaskbarEdge == TaskbarEdge.Top ? 0 : screenHeight - height, screenWidth, height);
    }
}

public static class TaskbarAutoHidePolicy
{
    public static bool ShouldCollapse(bool autoHideEnabled, bool pointerOverTaskbar, bool startMenuVisible) =>
        autoHideEnabled && !pointerOverTaskbar && !startMenuVisible;
}
