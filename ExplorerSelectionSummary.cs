using System.IO;

namespace DesktopTuner;

public sealed record ExplorerSelectionSummary(string Name, string Type, string Location, string Size, string Modified, string Created, string Accessed);

public static class ExplorerSelectionSummaryService
{
    public static ExplorerSelectionSummary Resolve(IEnumerable<ExplorerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var selection = entries.ToArray();
        if (selection.Length == 0) throw new ArgumentException("A selection summary requires at least one entry.", nameof(entries));
        if (selection.Length == 1) return ResolveSingle(selection[0]);

        var files = selection.Count(entry => !entry.IsDirectory && !entry.IsDrive);
        var folders = selection.Count(entry => entry.IsDirectory && !entry.IsDrive);
        var drives = selection.Length - files - folders;
        var types = new List<string>();
        if (files > 0) types.Add(Pluralize(files, "file"));
        if (folders > 0) types.Add(Pluralize(folders, "folder"));
        if (drives > 0) types.Add(Pluralize(drives, "drive"));

        var locations = selection
            .Select(entry => entry.IsDrive ? null : Path.GetDirectoryName(entry.FullPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var location = locations.Length == 1 && locations[0] is not null ? locations[0]! : "Multiple locations";

        var knownFileSizes = selection.Where(entry => !entry.IsDirectory && !entry.IsDrive && entry.Length.HasValue).Select(entry => entry.Length!.Value);
        var knownBytes = knownFileSizes.Aggregate(0L, (total, size) => total > long.MaxValue - size ? long.MaxValue : total + size);
        var unknownFiles = files - selection.Count(entry => !entry.IsDirectory && !entry.IsDrive && entry.Length.HasValue);
        var size = files == 0
            ? folders > 0 ? "Folder sizes not included" : "—"
            : unknownFiles == 0
                ? ExplorerEntry.FormatSize(knownBytes) + (folders > 0 ? " · folder sizes not included" : string.Empty)
                : $"{ExplorerEntry.FormatSize(knownBytes)} + {Pluralize(unknownFiles, "unknown size")}";

        var availableDates = selection.Where(entry => entry.Modified != DateTime.MinValue).Select(entry => entry.Modified.ToString("f")).Distinct(StringComparer.CurrentCulture).ToArray();
        var modified = availableDates.Length switch
        {
            0 => "—",
            1 when selection.All(entry => entry.Modified != DateTime.MinValue) => availableDates[0],
            1 => "Some dates unavailable",
            _ => "Multiple dates"
        };
        var created = ResolveCommonDate(selection.Select(entry => entry.Created));
        var accessed = ResolveCommonDate(selection.Select(entry => entry.Accessed));

        return new ExplorerSelectionSummary(
            $"{selection.Length:N0} items selected",
            string.Join(", ", types),
            location,
            size,
            modified,
            created,
            accessed);
    }

    private static ExplorerSelectionSummary ResolveSingle(ExplorerEntry entry) => new(
        entry.DisplayName,
        entry.Type,
        entry.FullPath,
        entry.SizeText.Length == 0 ? (entry.IsDirectory ? "Folder" : "—") : entry.SizeText,
        entry.Modified == DateTime.MinValue ? "—" : entry.Modified.ToString("f"),
        FormatOptionalDate(entry.Created),
        FormatOptionalDate(entry.Accessed));

    private static string ResolveCommonDate(IEnumerable<DateTime?> values)
    {
        var dates = values.Select(value => value is DateTime date && date != DateTime.MinValue ? date.ToString("f") : null).ToArray();
        var availableDates = dates.Where(date => date is not null).Distinct(StringComparer.CurrentCulture).ToArray();
        return availableDates.Length switch
        {
            0 => "—",
            1 when dates.All(date => date is not null) => availableDates[0]!,
            1 => "Some dates unavailable",
            _ => "Multiple dates"
        };
    }

    private static string FormatOptionalDate(DateTime? value) =>
        value is DateTime date && date != DateTime.MinValue ? date.ToString("f") : "—";

    private static string Pluralize(int count, string singular) => $"{count:N0} {singular}{(count == 1 ? string.Empty : "s")}";
}
