namespace DesktopTuner;

public static class TaskbarShortcutCatalog
{
    public static int? GetOneBasedPinIndex(uint virtualKey)
    {
        if (virtualKey is >= 0x31 and <= 0x39) return (int)(virtualKey - 0x30);
        if (virtualKey is >= 0x61 and <= 0x69) return (int)(virtualKey - 0x60);
        return null;
    }
}
