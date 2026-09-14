using System.Windows;

namespace DesktopTuner;

public readonly record struct TouchMenuMetrics(Thickness Padding, double MinimumHeight);

public static class TouchTargetPolicy
{
    private static readonly TouchMenuMetrics MouseMetrics = new(new Thickness(10, 7, 10, 7), 32);
    private static readonly TouchMenuMetrics TouchMetrics = new(new Thickness(14, 11, 14, 11), 44);

    public static TouchMenuMetrics Resolve(bool hasTouchInput) => hasTouchInput ? TouchMetrics : MouseMetrics;

    public static bool HasTouchInput => System.Windows.Input.Tablet.TabletDevices
        .Cast<System.Windows.Input.TabletDevice>()
        .Any(device => device.Type == System.Windows.Input.TabletDeviceType.Touch);
}
