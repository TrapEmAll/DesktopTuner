namespace DesktopTuner;

public static class ShellFileOperationPolicy
{
    private const uint AllowUndo = 0x00000040;
    private const uint WantNukeWarning = 0x00004000;

    public static uint GetDeleteFlags(bool permanentDelete) => permanentDelete ? WantNukeWarning : AllowUndo;
}
