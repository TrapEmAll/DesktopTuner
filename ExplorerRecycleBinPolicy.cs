namespace DesktopTuner;

public static class ExplorerRecycleBinPolicy
{
    public static bool ShouldShowEmptyCommand(bool isRecycleBinLocation) => isRecycleBinLocation;

    public static bool CanEmpty(bool isRecycleBinLocation, int itemCount)
    {
        if (itemCount < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
        return isRecycleBinLocation && itemCount > 0;
    }
}
