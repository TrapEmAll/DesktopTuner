using System.IO;
using System.Windows.Media;

namespace DesktopTuner;

public sealed record ExplorerEntry(string Name, string FullPath, bool IsDirectory, bool IsDrive, long? Length, DateTime Modified)
{
    public bool IsReparsePoint { get; init; }
    public bool IsHidden { get; init; }
    public bool IsSystem { get; init; }
    public bool IsRecycleBinItem { get; init; }
    public bool IsCut { get; init; }
    public DriveType? DriveType { get; init; }
    public int DriveGroupOrder { get; init; }
    public string? DriveGroup { get; init; }
    public long? DriveCapacityBytes { get; init; }
    public long? DriveFreeBytes { get; init; }
    public DateTime? RecentAccessed { get; init; }
    public DateTime? Created { get; init; }
    public DateTime? Accessed { get; init; }
    public string? ShellItemPath { get; init; }
    public string? OriginalLocation { get; init; }
    public DateTime? RecycleDeleted { get; init; }
    public string DisplayName { get; init; } = Name;
    public ImageSource? Icon => TaskbarIconService.LoadIcon(FullPath);
    public string Type => IsDrive ? ExplorerDriveCatalog.GetTypeName(DriveType ?? System.IO.DriveType.Unknown) : IsDirectory ? "File folder" : Path.GetExtension(IsRecycleBinItem ? Name : FullPath) is { Length: > 1 } extension ? $"{extension[1..].ToUpperInvariant()} file" : "File";
    public string SizeText => Length is long length ? FormatSize(length) : "";
    public double DriveUsagePercent => ExplorerDriveCatalog.GetUsagePercent(DriveCapacityBytes, DriveFreeBytes);
    public string DriveSpaceText => ExplorerDriveCatalog.GetSpaceSummary(DriveCapacityBytes, DriveFreeBytes);
    public bool HasDriveSpace => IsDrive && DriveSpaceText.Length > 0;
    public string ModifiedText => IsRecycleBinItem && RecycleDeleted is DateTime deleted ? deleted.ToString("g") : Modified == DateTime.MinValue ? "" : Modified.ToString("g");
    public string CreatedText => Created is DateTime created && created != DateTime.MinValue ? created.ToString("g") : "";
    public string AccessedText => Accessed is DateTime accessed && accessed != DateTime.MinValue ? accessed.ToString("g") : "";
    public string GetDisplayName(bool hideFileExtension) => hideFileExtension && !IsDirectory ? Path.GetFileNameWithoutExtension(Name) : Name;

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes:N0} B" : $"{size:N1} {units[unit]}";
    }
}
