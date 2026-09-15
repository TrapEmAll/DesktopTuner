namespace DesktopTuner;

public static class ShellOpenWithPolicy
{
    public static bool CanOpenWith(bool hasSingleSelection, bool isFolder, bool canShowNativeContextMenu) =>
        hasSingleSelection && !isFolder && canShowNativeContextMenu;
}
