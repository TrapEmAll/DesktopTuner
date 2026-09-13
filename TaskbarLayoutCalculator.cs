namespace DesktopTuner;

public sealed record TaskbarBounds(double Left, double Top, double Width, double Height);

public static class TaskbarLayoutCalculator
{
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
