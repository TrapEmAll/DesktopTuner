using System.Windows.Input;

namespace DesktopTuner;

public static class DesktopHostKeyboardPolicy
{
    public static bool ShouldDeleteSelection(Key key, ModifierKeys modifiers, bool hasSelection, bool isEditingName = false) =>
        key == Key.Delete && (modifiers is ModifierKeys.None or ModifierKeys.Shift) && hasSelection && !isEditingName;
}
