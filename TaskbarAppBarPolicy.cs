namespace DesktopTuner;

public static class TaskbarAppBarPolicy
{
    public static bool ShouldRegister(TaskbarStyle layout) => layout != TaskbarStyle.Floating;

    public static bool CanUseAsReplacement(TaskbarStyle layout, bool appBarRegistered, bool positionApproved) =>
        !ShouldRegister(layout) || appBarRegistered && positionApproved;

    public static bool ShouldRegisterAutoHide(bool appBarRegistered, bool autoHideEnabled) => appBarRegistered && autoHideEnabled;

    public static TaskbarBounds ProposeBounds(TaskbarDisplay display, TaskbarEdge edge, TaskbarBounds desiredBounds)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(desiredBounds);
        if (display.Width <= 0 || display.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(display), "Display dimensions must be positive.");
        if (!double.IsFinite(desiredBounds.Width) || !double.IsFinite(desiredBounds.Height)
            || desiredBounds.Width <= 0 || desiredBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(desiredBounds), "Appbar dimensions must be finite and positive.");

        return edge switch
        {
            TaskbarEdge.Left => new(display.Left, display.Top, Math.Min(desiredBounds.Width, display.Width), display.Height),
            TaskbarEdge.Right => new(display.Left + display.Width - Math.Min(desiredBounds.Width, display.Width), display.Top, Math.Min(desiredBounds.Width, display.Width), display.Height),
            TaskbarEdge.Top => new(display.Left, display.Top, display.Width, Math.Min(desiredBounds.Height, display.Height)),
            TaskbarEdge.Bottom => new(display.Left, display.Top + display.Height - Math.Min(desiredBounds.Height, display.Height), display.Width, Math.Min(desiredBounds.Height, display.Height)),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown taskbar edge.")
        };
    }

    public static TaskbarBounds PreserveThickness(TaskbarEdge edge, TaskbarBounds queriedBounds, TaskbarBounds desiredBounds)
    {
        ArgumentNullException.ThrowIfNull(queriedBounds);
        ArgumentNullException.ThrowIfNull(desiredBounds);
        return edge switch
        {
            TaskbarEdge.Left => queriedBounds with { Width = desiredBounds.Width },
            TaskbarEdge.Right => queriedBounds with { Left = queriedBounds.Left + queriedBounds.Width - desiredBounds.Width, Width = desiredBounds.Width },
            TaskbarEdge.Top => queriedBounds with { Height = desiredBounds.Height },
            TaskbarEdge.Bottom => queriedBounds with { Top = queriedBounds.Top + queriedBounds.Height - desiredBounds.Height, Height = desiredBounds.Height },
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown taskbar edge.")
        };
    }
}
