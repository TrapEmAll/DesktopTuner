namespace DesktopTuner;

public sealed record StartMenuAppListItem(AppEntry Application, string? AlphabetMarker);

public static class StartMenuAppListPolicy
{
    public static IReadOnlyList<StartMenuAppListItem> AddAlphabetMarkers(IEnumerable<AppEntry> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);
        string? previousMarker = null;
        return applications.Select(application =>
        {
            var marker = GetMarker(application.Name);
            var sectionMarker = string.Equals(marker, previousMarker, StringComparison.Ordinal) ? null : marker;
            previousMarker = marker;
            return new StartMenuAppListItem(application, sectionMarker);
        }).ToList();
    }

    private static string GetMarker(string name)
    {
        var firstVisibleCharacter = name.FirstOrDefault(character => !char.IsWhiteSpace(character));
        return firstVisibleCharacter == default || !char.IsLetter(firstVisibleCharacter)
            ? "#"
            : char.ToUpperInvariant(firstVisibleCharacter).ToString();
    }
}
