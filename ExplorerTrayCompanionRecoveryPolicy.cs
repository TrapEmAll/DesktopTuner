namespace DesktopTuner;

public static class ExplorerTrayCompanionRecoveryPolicy
{
    public const int MaximumRecoveryAttempts = 1;

    public static bool ShouldRestart(bool taskbarVisible, bool companionExited, int recoveryAttempts) =>
        !taskbarVisible && companionExited && recoveryAttempts < MaximumRecoveryAttempts;
}
