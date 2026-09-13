namespace DesktopTuner;

public static class TaskbarTrayIntegrationPolicy
{
    private const double MinimumOverlayWidth = 260;
    private const double BottomTraySearchBand = 160;

    public static TaskbarBounds? CalculateOverlayBounds(
        TaskbarDisplay display,
        DesktopPreferences preferences,
        TaskbarBounds? nativeTrayBounds,
        bool collapsed = false)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(preferences);
        if (nativeTrayBounds is not { } tray || preferences.TaskbarEdge != TaskbarEdge.Bottom ||
            preferences.TaskbarLayout == TaskbarStyle.Floating || display.ScaleX <= 0 || display.ScaleY <= 0)
            return null;

        var displayRight = display.Left + display.Width;
        var displayBottom = display.Top + display.Height;
        var trayRight = tray.Left + tray.Width;
        var trayBottom = tray.Top + tray.Height;
        if (tray.Width <= 0 || tray.Height <= 0 || tray.Left <= display.Left || tray.Left >= displayRight ||
            trayRight <= display.Left || tray.Top >= displayBottom || trayBottom < displayBottom - BottomTraySearchBand * display.ScaleY)
            return null;

        var overlayWidth = tray.Left - display.Left;
        if (overlayWidth < MinimumOverlayWidth * display.ScaleX) return null;

        var standardBounds = TaskbarLayoutCalculator.Calculate(display, preferences, collapsed);
        return standardBounds with { Width = Math.Min(overlayWidth, display.Width) };
    }
}
