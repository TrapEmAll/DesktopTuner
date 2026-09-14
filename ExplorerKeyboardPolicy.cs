using System.Windows.Input;

namespace DesktopTuner;

public enum ExplorerKeyboardAction
{
    None,
    OpenNewWindow,
    ReopenClosedTab,
    FocusAddress,
    FocusSearch,
    NavigateParent,
    NextPane,
    PreviousPane,
    ShowProperties
}

public static class ExplorerKeyboardPolicy
{
    public static ExplorerKeyboardAction Resolve(Key key, ModifierKeys modifiers, Key systemKey = Key.None)
    {
        if (key == Key.System) key = systemKey;
        return (key, modifiers) switch
        {
            (Key.N, ModifierKeys.Control) => ExplorerKeyboardAction.OpenNewWindow,
            (Key.T, ModifierKeys.Control | ModifierKeys.Shift) => ExplorerKeyboardAction.ReopenClosedTab,
            (Key.L, ModifierKeys.Control) or (Key.D, ModifierKeys.Alt) => ExplorerKeyboardAction.FocusAddress,
            (Key.F, ModifierKeys.Control) => ExplorerKeyboardAction.FocusSearch,
            (Key.Up, ModifierKeys.Alt) => ExplorerKeyboardAction.NavigateParent,
            (Key.F6, ModifierKeys.None) => ExplorerKeyboardAction.NextPane,
            (Key.F6, ModifierKeys.Shift) => ExplorerKeyboardAction.PreviousPane,
            (Key.Enter, ModifierKeys.Alt) => ExplorerKeyboardAction.ShowProperties,
            _ => ExplorerKeyboardAction.None
        };
    }
}
