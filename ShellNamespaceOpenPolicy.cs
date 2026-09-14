namespace DesktopTuner;

public enum ShellNamespaceOpenAction
{
    NavigateCurrentWindow,
    OpenCompanionWindow,
    UseShellHandler
}

public static class ShellNamespaceOpenPolicy
{
    public static ShellNamespaceOpenAction Resolve(bool isFolder, int selectionCount)
    {
        if (selectionCount < 1) throw new ArgumentOutOfRangeException(nameof(selectionCount));
        if (selectionCount == 1) return isFolder
            ? ShellNamespaceOpenAction.NavigateCurrentWindow
            : ShellNamespaceOpenAction.UseShellHandler;
        return isFolder
            ? ShellNamespaceOpenAction.OpenCompanionWindow
            : ShellNamespaceOpenAction.UseShellHandler;
    }
}
