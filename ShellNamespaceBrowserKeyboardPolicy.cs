using System.Windows.Input;

namespace DesktopTuner;

public enum ShellNamespaceBrowserKeyboardAction
{
    None,
    SelectAll,
    ClearSelection,
    ShowContextMenu
}

public static class ShellNamespaceBrowserKeyboardPolicy
{
    public static ShellNamespaceBrowserKeyboardAction Resolve(Key key, ModifierKeys modifiers, bool itemListFocused, bool hasSelection)
    {
        if (!itemListFocused) return ShellNamespaceBrowserKeyboardAction.None;
        if (key == Key.A && modifiers == ModifierKeys.Control) return ShellNamespaceBrowserKeyboardAction.SelectAll;
        if (key == Key.Escape && modifiers == ModifierKeys.None && hasSelection) return ShellNamespaceBrowserKeyboardAction.ClearSelection;
        if ((key == Key.Apps && modifiers == ModifierKeys.None) || (key == Key.F10 && modifiers == ModifierKeys.Shift))
            return ShellNamespaceBrowserKeyboardAction.ShowContextMenu;
        return ShellNamespaceBrowserKeyboardAction.None;
    }
}
