using System.IO;

namespace DesktopTuner;

public static class StartPinCatalog
{
    public const int MaximumPins = 24;
    public const string DefaultGroupName = "Pinned";
    public const int MaximumGroupNameLength = 32;

    public static IReadOnlyList<AppEntry> Normalize(IEnumerable<AppEntry>? apps)
    {
        var pins = (apps ?? [])
            .Where(IsSupported)
            .Select(app => app with
            {
                Name = app.Name.Trim(),
                CategoryPath = app.CategoryPath ?? string.Empty,
                TileSize = Enum.IsDefined(app.TileSize) ? app.TileSize : StartTileSize.Medium,
                GroupName = NormalizeGroupName(app.GroupName)
            })
            .DistinctBy(app => app.ShortcutPath, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumPins)
            .ToList();
        var canonicalNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return pins.Select(pin =>
        {
            if (!canonicalNames.TryGetValue(pin.GroupName, out var canonicalName))
                canonicalNames[pin.GroupName] = canonicalName = pin.GroupName;
            return pin with { GroupName = canonicalName };
        }).ToList();
    }

    public static IReadOnlyList<AppEntry> Pin(IEnumerable<AppEntry>? current, AppEntry app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!IsSupported(app)) throw new ArgumentException("Only Start menu shortcuts and packaged Windows apps can be pinned.", nameof(app));
        var pins = Normalize(current);
        if (pins.Any(pin => string.Equals(pin.ShortcutPath, app.ShortcutPath, StringComparison.OrdinalIgnoreCase)) || pins.Count >= MaximumPins)
            return pins;
        return [.. pins, app];
    }

    public static IReadOnlyList<AppEntry> AddDroppedFiles(IEnumerable<AppEntry>? current, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var pins = Normalize(current);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) continue;
            var extension = Path.GetExtension(path);
            if (!IsShortcutExtension(extension)) continue;

            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name)) name = path;
            pins = Pin(pins, new AppEntry(name, path));
        }
        return pins;
    }

    public static IReadOnlyList<AppEntry> Unpin(IEnumerable<AppEntry>? current, string shortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        return Normalize(current)
            .Where(pin => !string.Equals(pin.ShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<AppEntry> Move(IEnumerable<AppEntry>? current, string shortcutPath, int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        if (offset is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(offset), "Pins can move one position earlier or later.");

        var pins = Normalize(current).ToList();
        var index = pins.FindIndex(pin => string.Equals(pin.ShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase));
        var targetIndex = index + offset;
        if (index < 0 || targetIndex < 0 || targetIndex >= pins.Count) return pins;
        (pins[index], pins[targetIndex]) = (pins[targetIndex], pins[index]);
        return pins;
    }

    public static IReadOnlyList<AppEntry> SetTileSize(IEnumerable<AppEntry>? current, string shortcutPath, StartTileSize size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        if (!Enum.IsDefined(size)) throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown Start tile size.");
        return Normalize(current)
            .Select(pin => string.Equals(pin.ShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase)
                ? pin with { TileSize = size }
                : pin)
            .ToList();
    }

    public static IReadOnlyList<AppEntry> SetGroup(IEnumerable<AppEntry>? current, string shortcutPath, string groupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        var normalizedGroupName = NormalizeGroupName(groupName);
        return Normalize(current)
            .Select(pin => string.Equals(pin.ShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase)
                ? pin with { GroupName = normalizedGroupName }
                : pin)
            .ToList();
    }

    public static IReadOnlyList<AppEntry> RenameGroup(IEnumerable<AppEntry>? current, string currentGroupName, string newGroupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentGroupName);
        var normalizedCurrentGroupName = NormalizeGroupName(currentGroupName);
        var normalizedNewGroupName = NormalizeGroupName(newGroupName);
        return Normalize(current)
            .Select(pin => string.Equals(pin.GroupName, normalizedCurrentGroupName, StringComparison.OrdinalIgnoreCase)
                ? pin with { GroupName = normalizedNewGroupName }
                : pin)
            .ToList();
    }

    public static IReadOnlyList<StartPinGroup> Group(IEnumerable<AppEntry>? current) => Normalize(current)
        .GroupBy(pin => pin.GroupName, StringComparer.OrdinalIgnoreCase)
        .Select(group => new StartPinGroup(group.Key, group.ToList()))
        .ToList();

    public static string NormalizeGroupName(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return DefaultGroupName;
        var normalized = string.Concat(groupName.Trim().Where(character => !char.IsControl(character)));
        if (normalized.Length == 0) return DefaultGroupName;
        return normalized.Length <= MaximumGroupNameLength ? normalized : normalized[..MaximumGroupNameLength].TrimEnd();
    }

    public static IReadOnlyList<AppEntry> Reorder(IEnumerable<AppEntry>? current, string shortcutPath, int insertionIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);

        var pins = Normalize(current).ToList();
        var sourceIndex = pins.FindIndex(pin => string.Equals(pin.ShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0 || insertionIndex < 0 || insertionIndex > pins.Count) return pins;

        var app = pins[sourceIndex];
        pins.RemoveAt(sourceIndex);
        if (sourceIndex < insertionIndex) insertionIndex--;
        insertionIndex = Math.Clamp(insertionIndex, 0, pins.Count);
        pins.Insert(insertionIndex, app);
        return pins;
    }

    public static bool IsSupported(AppEntry? app) => app is not null &&
        !string.IsNullOrWhiteSpace(app.Name) &&
        !string.IsNullOrWhiteSpace(app.ShortcutPath) &&
        (app.IsPackagedApp
            ? app.ShortcutPath.Contains('!')
            : IsShortcutExtension(Path.GetExtension(app.ShortcutPath)));

    private static bool IsShortcutExtension(string extension) =>
        string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase);
}

public sealed record StartPinGroup(string Name, IReadOnlyList<AppEntry> Apps);
