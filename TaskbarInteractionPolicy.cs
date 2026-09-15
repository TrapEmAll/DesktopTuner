using System.Windows.Input;

namespace DesktopTuner;

public static class TaskbarInteractionPolicy
{
    public static bool ShouldLaunchNewPinnedInstance(MouseButton changedButton) => changedButton == MouseButton.Middle;
}
