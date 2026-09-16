using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTuner;

public static class AcrylicToolTipBehavior
{
    private static readonly ConditionalWeakTable<ToolTip, ToolTipState> States = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(AcrylicToolTipBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ToolTip toolTip) return;
        if (e.NewValue is true)
        {
            toolTip.Opened += ToolTip_Opened;
            toolTip.Closed += ToolTip_Closed;
        }
        else
        {
            toolTip.Opened -= ToolTip_Opened;
            toolTip.Closed -= ToolTip_Closed;
            RestoreBackground(toolTip);
        }
    }

    private static void ToolTip_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ToolTip toolTip)
            toolTip.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ApplyMaterial(toolTip)));
    }

    private static void ToolTip_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is ToolTip toolTip) RestoreBackground(toolTip);
    }

    private static void ApplyMaterial(ToolTip toolTip)
    {
        if (!toolTip.IsOpen || PresentationSource.FromVisual(toolTip) is not HwndSource source || source.Handle == IntPtr.Zero) return;
        SystemBackdropService.TryApplySmallRoundedCorners(source.Handle);
        if (!SystemBackdropService.TryApplyTransientBackdrop(source.Handle)) return;

        var state = States.GetValue(toolTip, static _ => new ToolTipState());
        if (state.BackgroundOverridden) return;
        state.OriginalBackground = toolTip.ReadLocalValue(Control.BackgroundProperty);
        state.BackgroundOverridden = true;
        toolTip.Background = Brushes.Transparent;
    }

    private static void RestoreBackground(ToolTip toolTip)
    {
        if (!States.TryGetValue(toolTip, out var state) || !state.BackgroundOverridden) return;
        if (ReferenceEquals(state.OriginalBackground, DependencyProperty.UnsetValue))
            toolTip.ClearValue(Control.BackgroundProperty);
        else
            toolTip.SetValue(Control.BackgroundProperty, state.OriginalBackground);
        state.BackgroundOverridden = false;
    }

    private sealed class ToolTipState
    {
        public object OriginalBackground { get; set; } = DependencyProperty.UnsetValue;
        public bool BackgroundOverridden { get; set; }
    }
}
