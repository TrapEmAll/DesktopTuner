namespace DesktopTuner;

public static class TaskbarPreviewLayoutPolicy
{
    public static TaskbarBounds Calculate(TaskbarDisplay display, TaskbarEdge edge, TaskbarBounds anchor, double previewWidthDip, double previewHeightDip)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (!Enum.IsDefined(edge)) throw new ArgumentOutOfRangeException(nameof(edge));
        if (display.Width <= 0 || display.Height <= 0 || display.ScaleX <= 0 || display.ScaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(display), "The display bounds and scale must be positive.");
        if (previewWidthDip <= 0 || previewHeightDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(previewWidthDip), "Preview dimensions must be positive.");

        var width = Math.Min(previewWidthDip * display.ScaleX, display.Width);
        var height = Math.Min(previewHeightDip * display.ScaleY, display.Height);
        var left = edge switch
        {
            TaskbarEdge.Left => anchor.Left + anchor.Width + 8,
            TaskbarEdge.Right => anchor.Left - width - 8,
            _ => anchor.Left + (anchor.Width - width) / 2
        };
        var top = edge switch
        {
            TaskbarEdge.Bottom => anchor.Top - height - 8,
            TaskbarEdge.Top => anchor.Top + anchor.Height + 8,
            _ => anchor.Top + (anchor.Height - height) / 2
        };
        left = Math.Clamp(left, display.Left, display.Left + display.Width - width);
        top = Math.Clamp(top, display.Top, display.Top + display.Height - height);
        return new TaskbarBounds(left, top, width, height);
    }
}
