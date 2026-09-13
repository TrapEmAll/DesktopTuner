namespace DesktopTuner;

public enum ExplorerViewMode
{
    Details,
    List,
    MediumIcons,
    LargeIcons,
    SmallIcons,
    Tiles
}

public sealed record ExplorerViewModeOption(ExplorerViewMode Mode, string Label, string? ItemTemplateKey, bool WrapItems);

public static class ExplorerViewModeCatalog
{
    public static IReadOnlyList<ExplorerViewModeOption> Options { get; } =
    [
        new(ExplorerViewMode.Details, "Details", null, false),
        new(ExplorerViewMode.List, "List", "ExplorerListItemTemplate", false),
        new(ExplorerViewMode.SmallIcons, "Small icons", "ExplorerSmallIconTemplate", false),
        new(ExplorerViewMode.MediumIcons, "Medium icons", "ExplorerMediumIconTemplate", true),
        new(ExplorerViewMode.LargeIcons, "Large icons", "ExplorerLargeIconTemplate", true),
        new(ExplorerViewMode.Tiles, "Tiles", "ExplorerTilesTemplate", true)
    ];

    public static ExplorerViewModeOption Get(ExplorerViewMode mode) =>
        Options.FirstOrDefault(option => option.Mode == mode)
        ?? throw new ArgumentOutOfRangeException(nameof(mode), mode, "The Explorer view mode is not supported.");
}
