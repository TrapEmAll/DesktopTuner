namespace DesktopTuner;

public sealed record ShellNamespaceViewModeOption(ExplorerViewMode Mode, string? ItemTemplateKey, bool WrapItems);

public static class ShellNamespaceViewModeCatalog
{
    public static IReadOnlyList<ShellNamespaceViewModeOption> Options { get; } =
    [
        new(ExplorerViewMode.Details, null, false),
        new(ExplorerViewMode.List, "ShellNamespaceListItemTemplate", false),
        new(ExplorerViewMode.SmallIcons, "ShellNamespaceSmallIconTemplate", false),
        new(ExplorerViewMode.MediumIcons, "ShellNamespaceMediumIconTemplate", true),
        new(ExplorerViewMode.LargeIcons, "ShellNamespaceLargeIconTemplate", true),
        new(ExplorerViewMode.Tiles, "ShellNamespaceTilesTemplate", true)
    ];

    public static ShellNamespaceViewModeOption Get(ExplorerViewMode mode) =>
        Options.FirstOrDefault(option => option.Mode == mode)
        ?? throw new ArgumentOutOfRangeException(nameof(mode), mode, "The Shell namespace view mode is not supported.");
}
