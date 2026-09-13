namespace DesktopTuner;

public static class TaskbarTrayIntegrationPolicy
{
    private const double MinimumOverlayLength = 260;
    private const double TraySearchBand = 160;

    public static TaskbarBounds? CalculateOverlayBounds(
        TaskbarDisplay display,
        DesktopPreferences preferences,
        TaskbarBounds? nativeTrayBounds,
        bool collapsed = false)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(preferences);
        if (nativeTrayBounds is not { } tray || preferences.TaskbarLayout == TaskbarStyle.Floating || display.ScaleX <= 0 || display.ScaleY <= 0)
            return null;

        var displayRight = display.Left + display.Width;
        var displayBottom = display.Top + display.Height;
        var trayRight = tray.Left + tray.Width;
        var trayBottom = tray.Top + tray.Height;
        if (tray.Width <= 0 || tray.Height <= 0 || tray.Left < display.Left || tray.Left >= displayRight ||
            trayRight <= display.Left || tray.Top < display.Top || trayBottom > displayBottom)
            return null;

        var standardBounds = TaskbarLayoutCalculator.Calculate(display, preferences, collapsed);
        if (preferences.TaskbarEdge is TaskbarEdge.Bottom or TaskbarEdge.Top)
        {
            var trayAtSelectedEdge = preferences.TaskbarEdge == TaskbarEdge.Bottom
                ? trayBottom >= displayBottom - TraySearchBand * display.ScaleY
                : tray.Top <= display.Top + TraySearchBand * display.ScaleY;
            var overlayWidth = tray.Left - display.Left;
            if (!trayAtSelectedEdge || overlayWidth < MinimumOverlayLength * display.ScaleX) return null;
            return standardBounds with { Width = Math.Min(overlayWidth, display.Width) };
        }

        var trayAtSelectedSide = preferences.TaskbarEdge == TaskbarEdge.Left
            ? tray.Left <= display.Left + TraySearchBand * display.ScaleX
            : trayRight >= displayRight - TraySearchBand * display.ScaleX;
        var trayAtBottom = trayBottom >= displayBottom - TraySearchBand * display.ScaleY;
        var overlayHeight = tray.Top - display.Top;
        if (!trayAtSelectedSide || !trayAtBottom || overlayHeight < MinimumOverlayLength * display.ScaleY) return null;
        return standardBounds with { Height = Math.Min(overlayHeight, display.Height) };
    }
}
