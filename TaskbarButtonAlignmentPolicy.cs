namespace DesktopTuner;

public static class TaskbarButtonAlignmentPolicy
{
    public static double CalculateLeadingSpacer(double viewportWidth, double buttonContentWidth, TaskbarButtonAlignment alignment)
    {
        if (!double.IsFinite(viewportWidth) || viewportWidth < 0) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (!double.IsFinite(buttonContentWidth) || buttonContentWidth < 0) throw new ArgumentOutOfRangeException(nameof(buttonContentWidth));
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));

        if (alignment == TaskbarButtonAlignment.Left) return 0;
        return Math.Max(0, (viewportWidth - buttonContentWidth) / 2);
    }
}
