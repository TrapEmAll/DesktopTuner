using System.IO;

namespace DesktopTuner;

public sealed record ExplorerEntry(string Name, string FullPath, bool IsDirectory, bool IsDrive, long? Length, DateTime Modified)
{
    public bool IsReparsePoint { get; init; }
    public bool IsHidden { get; init; }
    public bool IsSystem { get; init; }
    public bool IsCut { get; init; }
    public DateTime? RecentAccessed { get; init; }
    public string DisplayName { get; init; } = Name;
    public string Type => IsDrive ? "Local drive" : IsDirectory ? "File folder" : Path.GetExtension(FullPath) is { Length: > 1 } extension ? $"{extension[1..].ToUpperInvariant()} file" : "File";
    public string SizeText => Length is long length ? FormatSize(length) : "";
    public string ModifiedText => Modified == DateTime.MinValue ? "" : Modified.ToString("g");
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
