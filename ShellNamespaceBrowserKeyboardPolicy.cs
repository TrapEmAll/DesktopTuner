using System.Windows.Input;

namespace DesktopTuner;

public enum ShellNamespaceBrowserKeyboardAction
{
    None,
    SelectAll,
    ClearSelection,
    ShowContextMenu,
    Rename,
    ShowProperties
}

public static class ShellNamespaceBrowserKeyboardPolicy
{
    public static ShellNamespaceBrowserKeyboardAction Resolve(Key key, ModifierKeys modifiers, bool itemListFocused, bool hasSelection,
        bool canRename = false, Key systemKey = Key.None)
    {
        if (!itemListFocused) return ShellNamespaceBrowserKeyboardAction.None;
        if (key == Key.System) key = systemKey;
        if (key == Key.A && modifiers == ModifierKeys.Control) return ShellNamespaceBrowserKeyboardAction.SelectAll;
        if (key == Key.Escape && modifiers == ModifierKeys.None && hasSelection) return ShellNamespaceBrowserKeyboardAction.ClearSelection;
        if (key == Key.Enter && modifiers == ModifierKeys.Alt && hasSelection) return ShellNamespaceBrowserKeyboardAction.ShowProperties;
        if ((key == Key.Apps && modifiers == ModifierKeys.None) || (key == Key.F10 && modifiers == ModifierKeys.Shift))
            return ShellNamespaceBrowserKeyboardAction.ShowContextMenu;
        if (key == Key.F2 && modifiers == ModifierKeys.None && hasSelection && canRename)
            return ShellNamespaceBrowserKeyboardAction.Rename;
        return ShellNamespaceBrowserKeyboardAction.None;
    }
}
