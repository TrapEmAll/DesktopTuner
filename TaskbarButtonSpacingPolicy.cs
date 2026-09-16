using System.Windows;

namespace DesktopTuner;

public static class TaskbarButtonSpacingPolicy
{
    public static double GetGap(TaskbarButtonSpacing spacing) => spacing switch
    {
        TaskbarButtonSpacing.Compact => 1,
        TaskbarButtonSpacing.Standard => 2,
        TaskbarButtonSpacing.Relaxed => 4,
        TaskbarButtonSpacing.Wide => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(spacing))
    };

    public static Thickness GetButtonMargin(TaskbarButtonSpacing spacing, bool vertical)
    {
        var gap = GetGap(spacing);
        return vertical ? new Thickness(0, gap, 0, gap) : new Thickness(gap, 0, gap, 0);
    }
}
