using System.Windows.Input;

namespace DesktopTuner;

public static class TaskbarInteractionPolicy
{
    public static bool ShouldLaunchNewPinnedInstance(MouseButton changedButton) => changedButton == MouseButton.Middle;

    public static bool ShouldLaunchNewRunningInstance(MouseButton changedButton, bool hasLaunchablePath) =>
        changedButton == MouseButton.Middle && hasLaunchablePath;
}
