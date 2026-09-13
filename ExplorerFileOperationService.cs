using System.IO;

namespace DesktopTuner;

public static class ExplorerFileOperationService
{
    private const string NewFolderName = "New folder";

    private sealed record TransferPlan(string Source, string Destination, bool IsDirectory);

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

    public static IReadOnlyList<string> Transfer(IEnumerable<string> sourcePaths, string destinationDirectory, bool move)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var destination = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException($"The destination folder '{destination}' does not exist.");

        var plans = new List<TransferPlan>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourcePath in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var source = Path.GetFullPath(sourcePath);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(source); }
            catch (FileNotFoundException) { throw new FileNotFoundException("A selected item no longer exists.", source); }
            catch (DirectoryNotFoundException) { throw new FileNotFoundException("A selected item no longer exists.", source); }

            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A drive root cannot be transferred as an item.", nameof(sourcePaths));
            var target = Path.Combine(destination, name);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) continue;
            if (isDirectory && IsSameOrDescendant(source, destination))
                throw new IOException("A folder cannot be copied or moved into itself or one of its subfolders.");
            if (!destinations.Add(target) || File.Exists(target) || Directory.Exists(target))
                throw new IOException($"An item named '{name}' already exists in the destination folder.");
            var sameVolume = string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);
            if (isDirectory && (!move || !sameVolume)) EnsureDirectoryCanBeCopied(source);
            plans.Add(new TransferPlan(source, target, isDirectory));
        }

        foreach (var plan in plans)
        {
            if (move && string.Equals(Path.GetPathRoot(plan.Source), Path.GetPathRoot(plan.Destination), StringComparison.OrdinalIgnoreCase))
            {
                if (plan.IsDirectory) Directory.Move(plan.Source, plan.Destination);
                else File.Move(plan.Source, plan.Destination);
                continue;
            }

            if (plan.IsDirectory) CopyDirectoryContents(plan.Source, plan.Destination);
            else File.Copy(plan.Source, plan.Destination, overwrite: false);
            if (!move) continue;
            if (plan.IsDirectory) Directory.Delete(plan.Source, recursive: true);
            else File.Delete(plan.Source);
        }

        return plans.Select(plan => plan.Destination).ToList();
    }

    private static bool IsSameOrDescendant(string parent, string candidate)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "." || !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void EnsureDirectoryCanBeCopied(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new NotSupportedException($"Copying reparse-point folders is not supported: '{path}'.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var childAttributes = File.GetAttributes(entry);
            if ((childAttributes & FileAttributes.Directory) != 0) EnsureDirectoryCanBeCopied(entry);
        }
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new NotSupportedException($"Copying reparse-point folders is not supported: '{entry}'.");
                CopyDirectoryContents(entry, target);
            }
            else File.Copy(entry, target, overwrite: false);
        }
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
