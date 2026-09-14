using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public readonly record struct DesktopHostPosition(double Left, double Top);
public sealed record DesktopHostMonitorViewport(string DeviceName, double Left, double Top, double Width, double Height, bool IsPrimary);

public sealed class DesktopHostLayoutStore
{
    public const int MaximumItems = 4096;
    private const double ItemWidth = 100;
    private const double ItemHeight = 112;
    private readonly string _path;

    public DesktopHostLayoutStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopTuner", "desktop-host-layout.json");
    }

    public IReadOnlyList<DesktopHostItem> ApplyLayout(IEnumerable<DesktopHostItem> items, double viewportWidth, double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(items);
        var layout = ReadLayout();
        var remaining = items.DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.FullPath, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DesktopHostItem>(remaining.Count);
        foreach (var key in layout.Order)
            if (remaining.Remove(key, out var item)) ordered.Add(item);
        ordered.AddRange(remaining.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase));

        var width = double.IsFinite(viewportWidth) && viewportWidth > 0 ? viewportWidth : 1920;
        var height = double.IsFinite(viewportHeight) && viewportHeight > 0 ? viewportHeight : 1080;
        var rows = Math.Max(1, (int)Math.Floor(Math.Max(0, height - ItemHeight) / ItemHeight) + 1);
        var positioned = new List<DesktopHostItem>();
        foreach (var item in ordered)
        {
            if (layout.Positions.TryGetValue(item.FullPath, out var saved))
            {
                item.SetPosition(Clamp(saved, width, height));
                positioned.Add(item);
            }
        }
        var columns = Math.Max(1, (int)Math.Floor(Math.Max(0, width - ItemWidth) / ItemWidth) + 1);
        foreach (var item in ordered.Where(item => !layout.Positions.ContainsKey(item.FullPath)))
        {
            var position = FindAvailablePosition(positioned, columns, rows)
                ?? new DesktopHostPosition(Math.Min(positioned.Count / rows * ItemWidth, Math.Max(0, width - ItemWidth)),
                    Math.Min(positioned.Count % rows * ItemHeight, Math.Max(0, height - ItemHeight)));
            item.SetPosition(position);
            positioned.Add(item);
        }
        return ordered;
    }

    public IReadOnlyList<DesktopHostItem> ApplyOrder(IEnumerable<DesktopHostItem> items) => ApplyLayout(items, 1920, 1080);

    public IReadOnlyList<DesktopHostItem> ApplyMonitorLayout(IEnumerable<DesktopHostItem> items, IReadOnlyList<DesktopHostMonitorViewport> monitors)
    {
        ArgumentNullException.ThrowIfNull(items);
        ValidateMonitors(monitors);
        var layout = ReadLayout();
        var remaining = items.DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.FullPath, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DesktopHostItem>(remaining.Count);
        foreach (var key in layout.Order)
            if (remaining.Remove(key, out var item)) ordered.Add(item);
        ordered.AddRange(remaining.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase));

        var placed = new List<DesktopHostItem>();
        foreach (var item in ordered)
        {
            if (!layout.Positions.TryGetValue(item.FullPath, out var saved)) continue;
            var monitor = FindMonitor(monitors, saved.DeviceName) ?? (saved.DeviceName is null
                ? FindNearestMonitor(monitors, new DesktopHostPosition(saved.Left + ItemWidth / 2, saved.Top + ItemHeight / 2))
                : monitors.OrderByDescending(candidate => candidate.IsPrimary).First());
            var local = saved.DeviceName is null
                ? Clamp(new DesktopHostPosition(saved.Left - monitor.Left, saved.Top - monitor.Top), monitor.Width, monitor.Height)
                : Clamp(new DesktopHostPosition(saved.Left, saved.Top), monitor.Width, monitor.Height);
            item.SetPosition(new DesktopHostPosition(monitor.Left + local.Left, monitor.Top + local.Top));
            item.MonitorDeviceName = monitor.DeviceName;
            placed.Add(item);
        }

        var orderedMonitors = monitors.OrderByDescending(monitor => monitor.IsPrimary).ThenBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var item in ordered.Where(item => !layout.Positions.ContainsKey(item.FullPath)))
        {
            DesktopHostMonitorViewport? selectedMonitor = null;
            DesktopHostPosition? selectedPosition = null;
            foreach (var monitor in orderedMonitors)
            {
                selectedPosition = FindAvailablePosition(placed, monitor);
                if (selectedPosition is null) continue;
                selectedMonitor = monitor;
                break;
            }

            selectedMonitor ??= orderedMonitors[0];
            var local = selectedPosition ?? new DesktopHostPosition(0, 0);
            item.SetPosition(new DesktopHostPosition(selectedMonitor.Left + local.Left, selectedMonitor.Top + local.Top));
            item.MonitorDeviceName = selectedMonitor.DeviceName;
            placed.Add(item);
        }
        return ordered;
    }

    public static IReadOnlyDictionary<string, DesktopHostPosition> TranslateSelection(
        IEnumerable<DesktopHostItem> items,
        string anchorPath,
        DesktopHostPosition anchorPosition,
        double viewportWidth,
        double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(anchorPath);
        var selected = items.DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var anchor = selected.FirstOrDefault(item => string.Equals(item.FullPath, anchorPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("The drag anchor must be part of the selected items.", nameof(anchorPath));
        var width = double.IsFinite(viewportWidth) && viewportWidth > 0 ? viewportWidth : 1920;
        var height = double.IsFinite(viewportHeight) && viewportHeight > 0 ? viewportHeight : 1080;
        var clampedAnchor = Clamp(anchorPosition, width, height);
        var deltaX = clampedAnchor.Left - anchor.Left;
        var deltaY = clampedAnchor.Top - anchor.Top;
        return selected.ToDictionary(item => item.FullPath,
            item => Clamp(new DesktopHostPosition(item.Left + deltaX, item.Top + deltaY), width, height),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool SaveOrder(IEnumerable<DesktopHostItem> items) => SaveLayout(items);

    public bool SaveLayout(IEnumerable<DesktopHostItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var layout = CreateLayout(items, item => new PersistedPosition(item.Left, item.Top));
        return WriteLayout(layout);
    }

    public bool SaveMonitorLayout(IEnumerable<DesktopHostItem> items, IReadOnlyList<DesktopHostMonitorViewport> monitors)
    {
        ArgumentNullException.ThrowIfNull(items);
        ValidateMonitors(monitors);
        var layout = CreateLayout(items, item =>
        {
            var monitor = FindNearestMonitor(monitors,
                new DesktopHostPosition(item.Left + ItemWidth / 2, item.Top + ItemHeight / 2));
            var local = Clamp(new DesktopHostPosition(item.Left - monitor.Left, item.Top - monitor.Top), monitor.Width, monitor.Height);
            item.SetPosition(new DesktopHostPosition(monitor.Left + local.Left, monitor.Top + local.Top));
            item.MonitorDeviceName = monitor.DeviceName;
            return new PersistedPosition(local.Left, local.Top, monitor.DeviceName);
        });
        return WriteLayout(layout);
    }

    private PersistedLayout CreateLayout(IEnumerable<DesktopHostItem> items, Func<DesktopHostItem, PersistedPosition> positionSelector)
    {
        var entries = items.Take(MaximumItems).ToArray();
        return new PersistedLayout
        {
            Order = entries.Select(item => item.FullPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Positions = entries.Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
                .GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => positionSelector(group.First()), StringComparer.OrdinalIgnoreCase)
        };
    }

    private bool WriteLayout(PersistedLayout layout)
    {
        var temporaryPath = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? Environment.CurrentDirectory);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.TraceWarning($"Could not save desktop icon layout to '{_path}': {ex.Message}");
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Could not remove temporary desktop layout file '{temporaryPath}': {ex.Message}");
                }
            }
        }
    }

    private PersistedLayout ReadLayout()
    {
        if (!File.Exists(_path)) return new PersistedLayout();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var legacyOrder = JsonSerializer.Deserialize<string[]>(document.RootElement.GetRawText()) ?? [];
                return new PersistedLayout { Order = NormalizeOrder(legacyOrder) };
            }

            var values = JsonSerializer.Deserialize<PersistedLayout>(document.RootElement.GetRawText()) ?? new PersistedLayout();
            values.Order = NormalizeOrder(values.Order);
            values.Positions = (values.Positions ?? new Dictionary<string, PersistedPosition>(StringComparer.OrdinalIgnoreCase))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null &&
                    double.IsFinite(pair.Value.Left) && double.IsFinite(pair.Value.Top))
                .Take(MaximumItems)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            return values;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.TraceWarning($"Could not read desktop icon layout from '{_path}': {ex.Message}");
            return new PersistedLayout();
        }
    }

    private static List<string> NormalizeOrder(IEnumerable<string>? order) => (order ?? [])
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumItems).ToList();

    private static DesktopHostPosition Clamp(PersistedPosition position, double width, double height) =>
        Clamp(new DesktopHostPosition(position.Left, position.Top), width, height);

    private static DesktopHostPosition Clamp(DesktopHostPosition position, double width, double height) =>
        new(Math.Clamp(position.Left, 0, Math.Max(0, width - ItemWidth)), Math.Clamp(position.Top, 0, Math.Max(0, height - ItemHeight)));

    private static DesktopHostMonitorViewport? FindMonitor(IReadOnlyList<DesktopHostMonitorViewport> monitors, string? deviceName) =>
        string.IsNullOrWhiteSpace(deviceName) ? null : monitors.FirstOrDefault(monitor =>
            string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

    private static DesktopHostMonitorViewport FindNearestMonitor(IReadOnlyList<DesktopHostMonitorViewport> monitors, DesktopHostPosition point) =>
        DesktopHostDisplayLayoutPolicy.FindNearestMonitor(monitors, point);

    private static DesktopHostPosition? FindAvailablePosition(IReadOnlyList<DesktopHostItem> occupied, DesktopHostMonitorViewport monitor)
    {
        var width = Math.Max(1, (int)Math.Floor(monitor.Width / ItemWidth));
        var height = Math.Max(1, (int)Math.Floor(monitor.Height / ItemHeight));
        for (var column = 0; column < width; column++)
        for (var row = 0; row < height; row++)
        {
            var local = new DesktopHostPosition(column * ItemWidth, row * ItemHeight);
            var global = new DesktopHostPosition(monitor.Left + local.Left, monitor.Top + local.Top);
            if (occupied.All(item => !Overlaps(global, new DesktopHostPosition(item.Left, item.Top)))) return local;
        }
        return null;
    }

    private static void ValidateMonitors(IReadOnlyList<DesktopHostMonitorViewport> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0) throw new ArgumentException("At least one desktop monitor is required.", nameof(monitors));
        if (monitors.Any(monitor => string.IsNullOrWhiteSpace(monitor.DeviceName) || !double.IsFinite(monitor.Left) || !double.IsFinite(monitor.Top)
            || !double.IsFinite(monitor.Width) || !double.IsFinite(monitor.Height) || monitor.Width <= 0 || monitor.Height <= 0))
            throw new ArgumentException("Desktop monitor bounds must have a device name and positive finite dimensions.", nameof(monitors));
        if (monitors.Select(monitor => monitor.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != monitors.Count)
            throw new ArgumentException("Desktop monitor device names must be unique.", nameof(monitors));
    }

    private static DesktopHostPosition? FindAvailablePosition(IReadOnlyList<DesktopHostItem> occupied, int columns, int rows)
    {
        for (var column = 0; column < columns; column++)
        for (var row = 0; row < rows; row++)
        {
            var candidate = new DesktopHostPosition(column * ItemWidth, row * ItemHeight);
            if (occupied.All(item => !Overlaps(candidate, new DesktopHostPosition(item.Left, item.Top)))) return candidate;
        }
        return null;
    }

    private static bool Overlaps(DesktopHostPosition first, DesktopHostPosition second) =>
        first.Left < second.Left + ItemWidth && first.Left + ItemWidth > second.Left &&
        first.Top < second.Top + ItemHeight && first.Top + ItemHeight > second.Top;

    private sealed class PersistedLayout
    {
        public List<string> Order { get; set; } = [];
        public Dictionary<string, PersistedPosition> Positions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PersistedPosition(double Left, double Top, string? DeviceName = null);
}
