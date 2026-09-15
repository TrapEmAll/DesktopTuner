namespace DesktopTuner;

public static class TaskbarLabelVisibilityPolicy
{
    public static bool ShouldShow(TaskbarLabelVisibility visibility, int buttonCount, int visibleCapacity)
    {
        if (!Enum.IsDefined(visibility)) throw new ArgumentOutOfRangeException(nameof(visibility));
        if (buttonCount < 0) throw new ArgumentOutOfRangeException(nameof(buttonCount));
        if (visibleCapacity < 1) throw new ArgumentOutOfRangeException(nameof(visibleCapacity));
        return visibility switch
        {
            TaskbarLabelVisibility.Always => true,
            TaskbarLabelVisibility.WhenFull => buttonCount <= visibleCapacity,
            TaskbarLabelVisibility.Never => false,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility))
        };
    }
}
