using System.Windows.Input;

namespace DesktopTuner;

public static class StartMenuKeyboardPolicy
{
    public static bool ShouldShowContextMenu(Key key, ModifierKeys modifiers) =>
        (key == Key.F10 && modifiers == ModifierKeys.Shift) ||
        (key == Key.Apps && modifiers == ModifierKeys.None);
}
