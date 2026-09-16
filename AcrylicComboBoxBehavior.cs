using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTuner;

/// <summary>
/// Applies the same transient DWM material used by the app's menus to ComboBox dropdowns.
/// The original template background is restored when the popup closes or the behavior is disabled.
/// </summary>
public static class AcrylicComboBoxBehavior
{
    private static readonly ConditionalWeakTable<ComboBox, ComboState> States = new();
    private static readonly ConditionalWeakTable<Popup, ComboState> PopupStates = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(AcrylicComboBoxBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ComboBox combo) return;
        var state = States.GetValue(combo, static _ => new ComboState());
        if (e.NewValue is true)
        {
            combo.Loaded += ComboBox_Loaded;
            combo.Unloaded += ComboBox_Unloaded;
            if (combo.IsLoaded) AttachPopup(combo, state);
            return;
        }

        combo.Loaded -= ComboBox_Loaded;
        combo.Unloaded -= ComboBox_Unloaded;
        DetachPopup(state);
    }

    private static void ComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo)
            AttachPopup(combo, States.GetValue(combo, static _ => new ComboState()));
    }

    private static void ComboBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo && States.TryGetValue(combo, out var state))
            DetachPopup(state);
    }

    private static void AttachPopup(ComboBox combo, ComboState state)
    {
        combo.ApplyTemplate();
        if (combo.Template.FindName("PART_Popup", combo) is not Popup popup || ReferenceEquals(state.Popup, popup)) return;
        DetachPopup(state);
        state.Popup = popup;
        PopupStates.Add(popup, state);
        popup.Opened += Popup_Opened;
        popup.Closed += Popup_Closed;
    }

    private static void DetachPopup(ComboState state)
    {
        if (state.Popup is not { } popup) return;
        popup.Opened -= Popup_Opened;
        popup.Closed -= Popup_Closed;
        RestoreBackground(state);
        PopupStates.Remove(popup);
        state.Popup = null;
    }

    private static void Popup_Opened(object? sender, EventArgs e)
    {
        if (sender is not Popup popup || !popup.IsOpen) return;
        popup.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ApplyMaterial(popup)));
    }

    private static void Popup_Closed(object? sender, EventArgs e)
    {
        if (sender is Popup popup && FindState(popup) is { } state)
            RestoreBackground(state);
    }

    private static void ApplyMaterial(Popup popup)
    {
        if (!popup.IsOpen || popup.Child is not Visual child || PresentationSource.FromVisual(child) is not HwndSource source || source.Handle == IntPtr.Zero) return;
        if (FindState(popup) is not { } state) return;
        SystemBackdropService.TryApplySmallRoundedCorners(source.Handle);
        if (!SystemBackdropService.TryApplyTransientBackdrop(source.Handle)) return;

        if (popup.Child is Border border)
        {
            state.BackgroundOwner = border;
            state.OriginalBackground = border.ReadLocalValue(Border.BackgroundProperty);
            border.Background = Brushes.Transparent;
        }
        else if (popup.Child is Control control)
        {
            state.BackgroundOwner = control;
            state.OriginalBackground = control.ReadLocalValue(Control.BackgroundProperty);
            control.Background = Brushes.Transparent;
        }
    }

    private static void RestoreBackground(ComboState state)
    {
        if (state.BackgroundOwner is null || state.OriginalBackground is null) return;
        if (state.BackgroundOwner is Border border)
        {
            if (ReferenceEquals(state.OriginalBackground, DependencyProperty.UnsetValue)) border.ClearValue(Border.BackgroundProperty);
            else border.SetValue(Border.BackgroundProperty, state.OriginalBackground);
        }
        else if (state.BackgroundOwner is Control control)
        {
            if (ReferenceEquals(state.OriginalBackground, DependencyProperty.UnsetValue)) control.ClearValue(Control.BackgroundProperty);
            else control.SetValue(Control.BackgroundProperty, state.OriginalBackground);
        }
        state.BackgroundOwner = null;
        state.OriginalBackground = null;
    }

    private static ComboState? FindState(Popup popup)
        => PopupStates.TryGetValue(popup, out var state) ? state : null;

    private sealed class ComboState
    {
        public Popup? Popup { get; set; }
        public DependencyObject? BackgroundOwner { get; set; }
        public object? OriginalBackground { get; set; }
    }
}
