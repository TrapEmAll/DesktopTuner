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

    public static TaskbarBounds CalculateStartMenu(TaskbarDisplay display, double menuWidth, double menuHeight, DesktopPreferences preferences, bool centered = false)
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
            _ => preferences.TaskbarLayout == TaskbarStyle.Floating ? taskbar.Top + gapY : display.Top + gapY
        };
        if (centered)
        {
            if (preferences.TaskbarEdge is TaskbarEdge.Top or TaskbarEdge.Bottom)
                left = display.Left + (display.Width - width) / 2;
            else
                top = display.Top + (display.Height - height) / 2;
        }
        if (!centered && preferences.TaskbarLayout == TaskbarStyle.Floating && preferences.TaskbarEdge is TaskbarEdge.Top or TaskbarEdge.Bottom)
            left = taskbar.Left + gapX;

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
        TaskbarBounds bounds;
        if (vertical)
        {
            var width = collapsed ? 4 : preferences.TaskbarSize switch
            {
                TaskbarSize.Small => 150,
                TaskbarSize.Standard => 176,
                TaskbarSize.Large => 204,
                _ => throw new ArgumentOutOfRangeException(nameof(preferences), "Unknown taskbar size.")
            };
            var height = preferences.TaskbarLayout == TaskbarStyle.Floating ? screenHeight * 0.7 : screenHeight;
            var top = (screenHeight - height) / 2;
            var left = preferences.TaskbarEdge == TaskbarEdge.Left ? 0 : screenWidth - width;
            if (preferences.TaskbarLayout == TaskbarStyle.Floating && !collapsed)
                left += preferences.TaskbarEdge == TaskbarEdge.Left ? 12 : -12;
            bounds = new TaskbarBounds(left, top, width, height);
        }
        else
        {
            var height = collapsed ? 4 : preferences.TaskbarSize switch
            {
                TaskbarSize.Small => 46,
                TaskbarSize.Standard => 54,
                TaskbarSize.Large => 68,
                _ => throw new ArgumentOutOfRangeException(nameof(preferences), "Unknown taskbar size.")
            };
            var width = preferences.TaskbarLayout == TaskbarStyle.Floating ? screenWidth * 0.7 : screenWidth;
            var left = (screenWidth - width) / 2;
            var top = preferences.TaskbarEdge == TaskbarEdge.Top ? 0 : screenHeight - height;
            if (preferences.TaskbarLayout == TaskbarStyle.Floating && !collapsed)
                top += preferences.TaskbarEdge == TaskbarEdge.Top ? 12 : -12;
            bounds = new TaskbarBounds(left, top, width, height);
        }
        return bounds;
    }
}

public static class TaskbarAutoHidePolicy
{
    public static bool ShouldCollapse(bool autoHideEnabled, bool pointerOverTaskbar, bool startMenuVisible) =>
        autoHideEnabled && !pointerOverTaskbar && !startMenuVisible;

    public static bool ShouldCollapse(bool autoHideEnabled, bool autoHideWhenMaximized, bool maximizedWindowOnDisplay, bool pointerOverTaskbar, bool startMenuVisible) =>
        !startMenuVisible && !pointerOverTaskbar && (autoHideEnabled || autoHideWhenMaximized && maximizedWindowOnDisplay);

    public static bool HasMaximizedWindowOnDisplay(IEnumerable<RunningWindow> windows, TaskbarDisplay display)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(display);
        var right = display.Left + display.Width;
        var bottom = display.Top + display.Height;
        return windows.Any(window =>
        {
            if (!window.IsMaximized || !window.IsForeground) return false;
            var overlapWidth = Math.Max(0, Math.Min(window.Bounds.Left + window.Bounds.Width, right) - Math.Max(window.Bounds.Left, display.Left));
            var overlapHeight = Math.Max(0, Math.Min(window.Bounds.Top + window.Bounds.Height, bottom) - Math.Max(window.Bounds.Top, display.Top));
            return overlapWidth >= display.Width * 0.8 && overlapHeight >= display.Height * 0.8;
        });
    }
}

public static class TaskbarTransparencyPolicy
{
    public const int MaximumTransparency = 70;

    public static int Clamp(int transparencyPercent) => Math.Clamp(transparencyPercent, 0, MaximumTransparency);

    public static int GetEffectiveTransparency(int transparencyPercent, bool dynamicTransparency, bool maximizedWindowOnDisplay) =>
        dynamicTransparency && !maximizedWindowOnDisplay ? 0 : Clamp(transparencyPercent);

    public static byte GetAlpha(int transparencyPercent) =>
        (byte)Math.Round(byte.MaxValue * (100 - Clamp(transparencyPercent)) / 100d, MidpointRounding.AwayFromZero);
}
