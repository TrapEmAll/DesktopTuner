using System.IO;

namespace DesktopTuner;

public enum DesktopHostSortMode
{
    Name,
    ItemType,
    Size,
    DateModified
}

public sealed record DesktopHostLayoutPreferences(
    bool AutoArrange = false,
    bool AlignToGrid = false,
    DesktopHostSortMode SortMode = DesktopHostSortMode.Name);

public static class DesktopHostArrangementPolicy
{
    private const double ItemWidth = 100;
    private const double ItemHeight = 112;

    public static IReadOnlyList<DesktopHostItem> Sort(IEnumerable<DesktopHostItem> items, DesktopHostSortMode sortMode)
    {
        ArgumentNullException.ThrowIfNull(items);
        var source = items.ToArray();
        return sortMode switch
        {
            DesktopHostSortMode.Name => source.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            DesktopHostSortMode.ItemType => source.OrderBy(GetTypeKey, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            DesktopHostSortMode.Size => source.OrderBy(GetSize).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            DesktopHostSortMode.DateModified => source.OrderBy(GetModified).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(sortMode), sortMode, "Unknown desktop icon sort mode.")
        };
    }

    public static void Arrange(
        IEnumerable<DesktopHostItem> items,
        IReadOnlyList<DesktopHostMonitorViewport> monitors,
        DesktopHostSortMode sortMode) =>
        Place(items, monitors, Sort(items, sortMode), preserveGridCells: false);

    public static void AlignToGrid(IEnumerable<DesktopHostItem> items, IReadOnlyList<DesktopHostMonitorViewport> monitors)
    {
        ArgumentNullException.ThrowIfNull(items);
        var currentOrder = items.OrderBy(item => item.MonitorDeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Top).ThenBy(item => item.Left).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Place(currentOrder, monitors, currentOrder, preserveGridCells: true);
    }

    private static void Place(
        IEnumerable<DesktopHostItem> items,
        IReadOnlyList<DesktopHostMonitorViewport> monitors,
        IReadOnlyList<DesktopHostItem> ordered,
        bool preserveGridCells)
    {
        ArgumentNullException.ThrowIfNull(items);
        ValidateMonitors(monitors);
        var monitorOrder = monitors.OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray();
        var itemMonitors = ordered.ToDictionary(item => item.FullPath, item =>
        {
            var savedMonitor = monitorOrder.FirstOrDefault(monitor => string.Equals(
                monitor.DeviceName, item.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            return savedMonitor ?? DesktopHostDisplayLayoutPolicy.FindNearestMonitor(monitors,
                new DesktopHostPosition(item.Left + ItemWidth / 2, item.Top + ItemHeight / 2));
        }, StringComparer.OrdinalIgnoreCase);

        var assigned = new Dictionary<string, DesktopHostPosition>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in monitorOrder)
        {
            var onMonitor = ordered.Where(item => string.Equals(itemMonitors[item.FullPath].DeviceName,
                monitor.DeviceName, StringComparison.OrdinalIgnoreCase)).ToArray();
            var occupied = new HashSet<(int Column, int Row)>();
            foreach (var item in onMonitor)
            {
                var columns = Math.Max(1, (int)Math.Floor(monitor.Width / ItemWidth));
                var rows = Math.Max(1, (int)Math.Floor(monitor.Height / ItemHeight));
                var local = new DesktopHostPosition(item.Left - monitor.Left, item.Top - monitor.Top);
                var desiredColumn = preserveGridCells ? (int)Math.Round(local.Left / ItemWidth) : 0;
                var desiredRow = preserveGridCells ? (int)Math.Round(local.Top / ItemHeight) : 0;
                var cell = FindCell(occupied, columns, rows, desiredColumn, desiredRow, preserveGridCells);
                if (cell is not null) occupied.Add(cell.Value);
                var position = cell is { } placedCell
                    ? new DesktopHostPosition(placedCell.Column * ItemWidth, placedCell.Row * ItemHeight)
                    : new DesktopHostPosition(Math.Clamp(local.Left, 0, Math.Max(0, monitor.Width - ItemWidth)),
                        Math.Clamp(local.Top, 0, Math.Max(0, monitor.Height - ItemHeight)));
                assigned[item.FullPath] = new DesktopHostPosition(monitor.Left + position.Left, monitor.Top + position.Top);
                item.MonitorDeviceName = monitor.DeviceName;
            }
        }

        foreach (var item in ordered)
            if (assigned.TryGetValue(item.FullPath, out var position)) item.SetPosition(position);
    }

    private static (int Column, int Row)? FindCell(
        IReadOnlySet<(int Column, int Row)> occupied,
        int columns,
        int rows,
        int desiredColumn,
        int desiredRow,
        bool preferDesired)
    {
        var startColumn = Math.Clamp(desiredColumn, 0, columns - 1);
        var startRow = Math.Clamp(desiredRow, 0, rows - 1);
        var cells = Enumerable.Range(0, columns)
            .SelectMany(column => Enumerable.Range(0, rows).Select(row => (Column: column, Row: row)))
            .Where(cell => !occupied.Contains(cell)).ToArray();
        if (cells.Length == 0) return null;
        return preferDesired
            ? cells.OrderBy(cell => Math.Pow(cell.Column - startColumn, 2) + Math.Pow(cell.Row - startRow, 2))
                .ThenBy(cell => cell.Column).ThenBy(cell => cell.Row).First()
            : cells.OrderBy(cell => cell.Column).ThenBy(cell => cell.Row).First();
    }

    private static string GetTypeKey(DesktopHostItem item)
    {
        if (item.IsShellNamespace) return "System";
        if (item.IsDirectory) return "Folder";
        return Path.GetExtension(item.Name).TrimStart('.');
    }

    private static long? GetSize(DesktopHostItem item)
    {
        if (item.IsShellNamespace || item.IsDirectory) return null;
        try { return new FileInfo(item.FullPath).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    private static DateTime? GetModified(DesktopHostItem item)
    {
        if (item.IsShellNamespace) return null;
        try { return item.IsDirectory ? Directory.GetLastWriteTime(item.FullPath) : File.GetLastWriteTime(item.FullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    private static void ValidateMonitors(IReadOnlyList<DesktopHostMonitorViewport> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0) throw new ArgumentException("At least one desktop monitor is required.", nameof(monitors));
    }
}
