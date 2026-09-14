namespace DesktopTuner;

public static class ExplorerRenamePolicy
{
    public static int GetInitialSelectionLength(string name, bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (isDirectory) return name.Length;

        var extensionStart = name.LastIndexOf('.');
        return extensionStart > 0 ? extensionStart : name.Length;
    }
}
