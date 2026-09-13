namespace DesktopTuner;

public static class TaskbarKeyboardNavigationPolicy
{
    public static int? GetAdjacentIndex(int currentIndex, int itemCount, bool forward)
    {
        if (itemCount < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
        if (itemCount == 0) return null;
        if (currentIndex < -1 || currentIndex >= itemCount) throw new ArgumentOutOfRangeException(nameof(currentIndex));

        var origin = currentIndex < 0 ? forward ? itemCount - 1 : 0 : currentIndex;
        return (origin + (forward ? 1 : -1) + itemCount) % itemCount;
    }
}
