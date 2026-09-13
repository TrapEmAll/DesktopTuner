using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTuner;

public static class AcrylicContextMenuBehavior
{
    private static readonly ConditionalWeakTable<ContextMenu, MenuState> MenuStates = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(AcrylicContextMenuBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ContextMenu menu) return;
        if (e.NewValue is true)
        {
            menu.Opened += ContextMenu_Opened;
            return;
        }

        menu.Opened -= ContextMenu_Opened;
        RestoreSolidBackground(menu);
    }

    private static void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ApplyMaterial(menu)));
    }

    private static void ApplyMaterial(ContextMenu menu)
    {
        if (!menu.IsOpen || PresentationSource.FromVisual(menu) is not HwndSource source || source.Handle == IntPtr.Zero) return;
        SystemBackdropService.TryApplyRoundedMenuCorners(source.Handle);
        if (!SystemBackdropService.TryApplyTransientBackdrop(source.Handle)) return;

        var state = MenuStates.GetValue(menu, static _ => new MenuState());
        if (state.BackgroundOverridden) return;
        state.OriginalBackground = menu.ReadLocalValue(Control.BackgroundProperty);
        state.BackgroundOverridden = true;
        menu.Background = Brushes.Transparent;
    }

    private static void RestoreSolidBackground(ContextMenu menu)
    {
        if (!MenuStates.TryGetValue(menu, out var state) || !state.BackgroundOverridden) return;
        if (ReferenceEquals(state.OriginalBackground, DependencyProperty.UnsetValue))
            menu.ClearValue(Control.BackgroundProperty);
        else
            menu.SetValue(Control.BackgroundProperty, state.OriginalBackground);
        state.BackgroundOverridden = false;
    }

    private sealed class MenuState
    {
        public object OriginalBackground { get; set; } = DependencyProperty.UnsetValue;
        public bool BackgroundOverridden { get; set; }
    }
}
