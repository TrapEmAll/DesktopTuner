namespace DesktopTuner;

public sealed record StartMenuPlace(string Id, string Label);

public static class StartMenuPlaceCatalog
{
    public static IReadOnlyList<StartMenuPlace> AdditionalPlaces { get; } =
    [
        new("computer", "This PC"),
        new("control-panel", "Control Panel"),
        new("network", "Network"),
        new("music", "Music"),
        new("pictures", "Pictures"),
        new("videos", "Videos"),
        new("recent", "Recent items")
    ];

    public static string ResolveTarget(string id) => id switch
    {
        "computer" => "shell:MyComputerFolder",
        "control-panel" => "control.exe",
        "network" => "shell:NetworkPlacesFolder",
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "recent" => Environment.GetFolderPath(Environment.SpecialFolder.Recent),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown Start menu place.")
    };
}
