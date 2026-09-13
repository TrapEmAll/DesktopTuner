using System.IO;

namespace DesktopTuner;

public static class ExplorerFileOperationService
{
    private const string NewFolderName = "New folder";

    public static string CreateFolder(string parentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        if (!Directory.Exists(parentDirectory)) throw new DirectoryNotFoundException($"The parent folder '{parentDirectory}' does not exist.");

        var name = NewFolderName;
        for (var suffix = 2; Directory.Exists(Path.Combine(parentDirectory, name)) || File.Exists(Path.Combine(parentDirectory, name)); suffix++)
            name = $"{NewFolderName} ({suffix})";
        return Directory.CreateDirectory(Path.Combine(parentDirectory, name)).FullName;
    }

    public static string Rename(string path, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateName(newName);
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("A drive root cannot be renamed.");
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) throw new FileNotFoundException("The selected item no longer exists.", fullPath);

        var target = Path.Combine(parent, newName);
        if (string.Equals(fullPath, target, StringComparison.OrdinalIgnoreCase)) return fullPath;
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException($"An item named '{newName}' already exists in this folder.");

        if (Directory.Exists(fullPath)) Directory.Move(fullPath, target);
        else File.Move(fullPath, target);
        return target;
    }

    public static void ValidateName(string? name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.'))
            throw new ArgumentException("Names cannot be '.', '..', or end with a space or period.", nameof(name));
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Names cannot contain characters reserved by Windows file names.", nameof(name));
    }
}
