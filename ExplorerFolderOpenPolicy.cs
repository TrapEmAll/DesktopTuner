namespace DesktopTuner;

public static class ExplorerFolderOpenPolicy
{
    public static bool ShouldOpenInNewTab(bool openFoldersInNewTab, bool isDirectory, bool isDrive) =>
        openFoldersInNewTab && isDirectory && !isDrive;
}
