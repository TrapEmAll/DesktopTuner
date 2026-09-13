using System.IO;

namespace DesktopTuner;

public static class TaskbarPinCatalog
{
    public const int MaximumPins = 40;

    public static bool IsSupportedTarget(string itemPath, bool isDirectory = false) =>
        Path.IsPathFullyQualified(itemPath) && (isDirectory || IsLaunchableExtension(Path.GetExtension(itemPath)));

    public static List<PinnedTaskbarApp> Add(IEnumerable<PinnedTaskbarApp> current, string name, string itemPath, bool isDirectory = false)
    {
        var pins = current.ToList();
        if (string.IsNullOrWhiteSpace(name) || !IsSupportedTarget(itemPath, isDirectory) ||
            pins.Any(app => string.Equals(app.ExecutablePath, itemPath, StringComparison.OrdinalIgnoreCase)) ||
            pins.Count >= MaximumPins)
            return pins;

        pins.Add(new PinnedTaskbarApp(name, itemPath, isDirectory));
        return pins;
    }

    public static List<PinnedTaskbarApp> AddDroppedFiles(IEnumerable<PinnedTaskbarApp> current, IEnumerable<string> paths, Func<string, bool>? isDirectory = null)
    {
        var pins = current.ToList();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                continue;

            var directory = isDirectory?.Invoke(path) ?? false;
            var extension = Path.GetExtension(path);
            if (!directory && !IsLaunchableExtension(extension)) continue;

            var name = directory
                ? Path.GetFileName(Path.TrimEndingDirectorySeparator(path))
                : Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name)) name = path;
            pins = Add(pins, name, path, directory);
        }
        return pins;
    }

    public static List<PinnedTaskbarApp> Remove(IEnumerable<PinnedTaskbarApp> current, string itemPath) =>
        current.Where(app => !string.Equals(app.ExecutablePath, itemPath, StringComparison.OrdinalIgnoreCase)).ToList();

    private static bool IsLaunchableExtension(string extension) =>
        string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
}
