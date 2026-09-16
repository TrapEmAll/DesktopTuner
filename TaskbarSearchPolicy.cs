namespace DesktopTuner;

public enum TaskbarSearchAction
{
    None,
    OpenWindowsSearch,
    SearchStartMenu
}

public static class TaskbarSearchPolicy
{
    public static TaskbarSearchAction ResolveEnterAction(string? query) =>
        string.IsNullOrWhiteSpace(query) ? TaskbarSearchAction.OpenWindowsSearch : TaskbarSearchAction.SearchStartMenu;

    public static bool ShouldClearOnEscape(string? query) => !string.IsNullOrEmpty(query);
}
