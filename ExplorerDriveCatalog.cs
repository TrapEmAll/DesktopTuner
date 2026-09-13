using System.IO;

namespace DesktopTuner;

public static class ExplorerDriveCatalog
{
    public static (int Order, string Name) GetGroup(DriveType driveType) => driveType switch
    {
        DriveType.Fixed => (0, "Hard disk drives"),
        DriveType.Removable => (1, "Devices with removable storage"),
        DriveType.CDRom => (2, "Optical drives"),
        DriveType.Network => (3, "Network locations"),
        DriveType.Ram => (4, "RAM drives"),
        _ => (5, "Other drives")
    };

    public static string GetTypeName(DriveType driveType) => driveType switch
    {
        DriveType.Fixed => "Local disk",
        DriveType.Removable => "Removable disk",
        DriveType.CDRom => "CD drive",
        DriveType.Network => "Network drive",
        DriveType.Ram => "RAM disk",
        _ => "Drive"
    };

    public static IReadOnlyList<ExplorerEntry> Sort(
        IEnumerable<ExplorerEntry> entries,
        ExplorerSortColumn column,
        bool ascending)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!Enum.IsDefined(column)) throw new ArgumentOutOfRangeException(nameof(column));

        IOrderedEnumerable<ExplorerEntry> grouped = entries.OrderBy(entry => entry.DriveGroupOrder);
        grouped = column switch
        {
            ExplorerSortColumn.Name => ascending
                ? grouped.ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                : grouped.ThenByDescending(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            ExplorerSortColumn.DateModified => ascending ? grouped.ThenBy(entry => entry.Modified) : grouped.ThenByDescending(entry => entry.Modified),
            ExplorerSortColumn.Type => ascending
                ? grouped.ThenBy(entry => entry.Type, StringComparer.CurrentCultureIgnoreCase)
                : grouped.ThenByDescending(entry => entry.Type, StringComparer.CurrentCultureIgnoreCase),
            ExplorerSortColumn.Size => ascending ? grouped.ThenBy(entry => entry.Length) : grouped.ThenByDescending(entry => entry.Length),
            _ => throw new ArgumentOutOfRangeException(nameof(column))
        };
        return grouped.ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
