using System.Windows.Input;

namespace DesktopTuner;

public enum ExplorerKeyboardAction
{
    None,
    FocusAddress,
    FocusSearch,
    NextPane,
    PreviousPane
}

public static class ExplorerKeyboardPolicy
{
    public static ExplorerKeyboardAction Resolve(Key key, ModifierKeys modifiers, Key systemKey = Key.None)
    {
        if (key == Key.System) key = systemKey;
        return (key, modifiers) switch
        {
            (Key.L, ModifierKeys.Control) or (Key.D, ModifierKeys.Alt) => ExplorerKeyboardAction.FocusAddress,
            (Key.F, ModifierKeys.Control) => ExplorerKeyboardAction.FocusSearch,
            (Key.F6, ModifierKeys.None) => ExplorerKeyboardAction.NextPane,
            (Key.F6, ModifierKeys.Shift) => ExplorerKeyboardAction.PreviousPane,
            _ => ExplorerKeyboardAction.None
        };
    }
}
