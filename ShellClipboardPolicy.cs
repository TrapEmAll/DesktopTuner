namespace DesktopTuner;

public static class ShellClipboardPolicy
{
    private const uint DropEffectMove = 0x00000002;

    public static bool IsCutDropEffect(uint effect) => effect == DropEffectMove;
}
