using System.Windows.Input;

namespace DesktopTuner;

public enum ExplorerMouseNavigationAction
{
    None,
    Back,
    Forward
}

public static class ExplorerMouseNavigationPolicy
{
    public static ExplorerMouseNavigationAction Resolve(MouseButton button) => button switch
    {
        MouseButton.XButton1 => ExplorerMouseNavigationAction.Back,
        MouseButton.XButton2 => ExplorerMouseNavigationAction.Forward,
        _ => ExplorerMouseNavigationAction.None
    };
}
