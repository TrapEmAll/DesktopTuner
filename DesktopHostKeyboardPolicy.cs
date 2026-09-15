using System.Windows.Input;

namespace DesktopTuner;

public enum DesktopHostClipboardAction
{
    None,
    Copy,
    Cut,
    Paste
}

public static class DesktopHostKeyboardPolicy
{
    public static bool ShouldCreateFolder(Key key, ModifierKeys modifiers, bool isEditingName = false) =>
        key == Key.N && modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && !isEditingName;

    public static bool ShouldShowProperties(Key key, ModifierKeys modifiers, bool hasSelection, bool isEditingName = false, Key systemKey = Key.None)
    {
        if (key == Key.System) key = systemKey;
        return key == Key.Enter && modifiers == ModifierKeys.Alt && hasSelection && !isEditingName;
    }

    public static bool ShouldDeleteSelection(Key key, ModifierKeys modifiers, bool hasSelection, bool isEditingName = false) =>
        key == Key.Delete && (modifiers is ModifierKeys.None or ModifierKeys.Shift) && hasSelection && !isEditingName;

    public static DesktopHostClipboardAction ResolveClipboardAction(Key key, ModifierKeys modifiers, bool hasSelection, bool isEditingName = false)
    {
        if (modifiers != ModifierKeys.Control || isEditingName) return DesktopHostClipboardAction.None;
        return key switch
        {
            Key.C when hasSelection => DesktopHostClipboardAction.Copy,
            Key.X when hasSelection => DesktopHostClipboardAction.Cut,
            Key.V => DesktopHostClipboardAction.Paste,
            _ => DesktopHostClipboardAction.None
        };
    }
}
