using System.Diagnostics;
using System.IO;

namespace DesktopTuner;

public static class TaskbarPinCatalog
{
    public const int MaximumPins = 40;
    private static readonly HashSet<string> NonLaunchableShellHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost.exe", "RuntimeBroker.exe", "SearchHost.exe", "StartMenuExperienceHost.exe",
        "ShellExperienceHost.exe", "explorer.exe"
    };

    public static bool CanRunAsAdministrator(string name, string executablePath, bool isDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        return !isDirectory && !NonLaunchableShellHosts.Contains(Path.GetFileName(executablePath))
            && AppCatalogService.CanRunAsAdministrator(new AppEntry(name, executablePath));
    }

    public static bool CanOpenLocation(PinnedTaskbarApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.IsDirectory
            ? Directory.Exists(app.ExecutablePath)
            : AppCatalogService.CanOpenFileLocation(new AppEntry(app.Name, app.ExecutablePath));
    }

    public static ProcessStartInfo BuildLocationLaunchInfo(PinnedTaskbarApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!CanOpenLocation(app))
            throw new NotSupportedException("This taskbar pin does not have an available file location.");

        return app.IsDirectory
            ? new ProcessStartInfo(app.ExecutablePath) { UseShellExecute = true }
            : AppCatalogService.BuildFileLocationLaunchInfo(new AppEntry(app.Name, app.ExecutablePath));
    }

    public static ProcessStartInfo BuildElevatedLaunchInfo(PinnedTaskbarApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!CanRunAsAdministrator(app.Name, app.ExecutablePath, app.IsDirectory))
            throw new NotSupportedException("This taskbar pin cannot be started with administrator privileges.");

        return AppCatalogService.BuildLaunchInfo(new AppEntry(app.Name, app.ExecutablePath), runAsAdministrator: true);
    }

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

    public static List<PinnedTaskbarApp> Move(IEnumerable<PinnedTaskbarApp> current, string sourcePath, string targetPath)
    {
        var pins = current.ToList();
        var sourceIndex = pins.FindIndex(app => string.Equals(app.ExecutablePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        var targetIndex = pins.FindIndex(app => string.Equals(app.ExecutablePath, targetPath, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return pins;

        var source = pins[sourceIndex];
        pins.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex) targetIndex--;
        pins.Insert(targetIndex, source);
        return pins;
    }

    private static bool IsLaunchableExtension(string extension) =>
        string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
}
