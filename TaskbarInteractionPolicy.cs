using System.Windows.Input;

namespace DesktopTuner;

public static class TaskbarInteractionPolicy
{
    public static bool ShouldShowContextMenu(Key key, ModifierKeys modifiers) =>
        key == Key.F10 && modifiers == ModifierKeys.Shift;

    public static bool ShouldShowSystemMenu(MouseButton changedButton, ModifierKeys modifiers) =>
        changedButton == MouseButton.Right && modifiers == ModifierKeys.Shift;

    public static bool ShouldShowProperties(Key key, ModifierKeys modifiers) =>
        key == Key.Enter && modifiers == ModifierKeys.Alt;

    public static bool ShouldShowProperties(MouseButton changedButton, ModifierKeys modifiers) =>
        changedButton == MouseButton.Left && modifiers == ModifierKeys.Alt;

    public static bool ShouldLaunchPinnedElevated(MouseButton changedButton, ModifierKeys modifiers) =>
        changedButton == MouseButton.Left && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

    public static bool ShouldLaunchNewPinnedInstance(MouseButton changedButton, ModifierKeys modifiers = ModifierKeys.None) =>
        changedButton == MouseButton.Middle || IsShiftClick(changedButton, modifiers);

    public static bool ShouldLaunchNewRunningInstance(MouseButton changedButton, bool hasLaunchablePath, ModifierKeys modifiers = ModifierKeys.None) =>
        hasLaunchablePath && (changedButton == MouseButton.Middle || IsShiftClick(changedButton, modifiers));

    public static bool ShouldCycleWindows(ModifierKeys modifiers) => modifiers == ModifierKeys.Control;

    private static bool IsShiftClick(MouseButton changedButton, ModifierKeys modifiers) =>
        changedButton == MouseButton.Left &&
        modifiers.HasFlag(ModifierKeys.Shift) &&
        (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == ModifierKeys.None;
}
