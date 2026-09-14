namespace DesktopTuner;

public static class TaskbarSnapshotOwnerPolicy
{
    public static bool IsSnapshotOwner(DateTime processStartedAtUtc, DateTime snapshotWrittenAtUtc) =>
        processStartedAtUtc <= snapshotWrittenAtUtc;
}
