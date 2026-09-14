namespace DesktopTuner;

public static class ShellMultiPropertiesPolicy
{
    public static bool ShouldUseMergedProperties(bool sameParent, bool allFileSystemItems, int selectionCount) =>
        !sameParent && allFileSystemItems && selectionCount > 1;
}
