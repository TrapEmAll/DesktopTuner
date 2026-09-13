using System.IO;

namespace DesktopTuner;

public static class TaskbarPinCatalog
{
    public const int MaximumPins = 40;

    public static List<PinnedTaskbarApp> Add(IEnumerable<PinnedTaskbarApp> current, string name, string executablePath)
    {
        var pins = current.ToList();
        if (string.IsNullOrWhiteSpace(name) || !Path.IsPathFullyQualified(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            pins.Any(app => string.Equals(app.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)) ||
            pins.Count >= MaximumPins)
            return pins;

        pins.Add(new PinnedTaskbarApp(name, executablePath));
        return pins;
    }

    public static List<PinnedTaskbarApp> Remove(IEnumerable<PinnedTaskbarApp> current, string executablePath) =>
        current.Where(app => !string.Equals(app.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)).ToList();
}
