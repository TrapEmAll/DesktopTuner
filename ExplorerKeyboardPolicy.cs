using System.Windows.Input;

namespace DesktopTuner;

public enum ExplorerKeyboardAction
{
    None,
    NavigateBack,
    NavigateForward,
    CreateFolder,
    OpenNewWindow,
    ReopenClosedTab,
    FocusAddress,
    FocusSearch,
    FocusNavigationTree,
    NavigateParent,
    NextPane,
    PreviousPane,
    ShowProperties,
    ShowContextMenu
}

public static class ExplorerKeyboardPolicy
{
    public static ExplorerKeyboardAction Resolve(Key key, ModifierKeys modifiers, Key systemKey = Key.None)
    {
        if (key == Key.System) key = systemKey;
        if ((key == Key.Apps && modifiers == ModifierKeys.None) || (key == Key.F10 && modifiers == ModifierKeys.Shift))
            return ExplorerKeyboardAction.ShowContextMenu;
        return (key, modifiers) switch
        {
            (Key.Left, ModifierKeys.Alt) => ExplorerKeyboardAction.NavigateBack,
            (Key.Right, ModifierKeys.Alt) => ExplorerKeyboardAction.NavigateForward,
            (Key.N, ModifierKeys.Control | ModifierKeys.Shift) => ExplorerKeyboardAction.CreateFolder,
            (Key.N, ModifierKeys.Control) => ExplorerKeyboardAction.OpenNewWindow,
            (Key.T, ModifierKeys.Control | ModifierKeys.Shift) => ExplorerKeyboardAction.ReopenClosedTab,
            (Key.L, ModifierKeys.Control) or (Key.D, ModifierKeys.Alt) => ExplorerKeyboardAction.FocusAddress,
            (Key.F4, ModifierKeys.None) => ExplorerKeyboardAction.FocusAddress,
            (Key.F, ModifierKeys.Control) => ExplorerKeyboardAction.FocusSearch,
            (Key.F3, ModifierKeys.None) => ExplorerKeyboardAction.FocusSearch,
            (Key.E, ModifierKeys.Control | ModifierKeys.Shift) => ExplorerKeyboardAction.FocusNavigationTree,
            (Key.Up, ModifierKeys.Alt) => ExplorerKeyboardAction.NavigateParent,
            (Key.F6, ModifierKeys.None) => ExplorerKeyboardAction.NextPane,
            (Key.F6, ModifierKeys.Shift) => ExplorerKeyboardAction.PreviousPane,
            (Key.Enter, ModifierKeys.Alt) => ExplorerKeyboardAction.ShowProperties,
            _ => ExplorerKeyboardAction.None
        };
    }
}
