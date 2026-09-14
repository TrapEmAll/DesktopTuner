using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public readonly record struct DesktopHostPosition(double Left, double Top);

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
        var rows = Math.Max(1, (int)Math.Floor(Math.Max(ItemHeight, height - ItemHeight) / ItemHeight));
        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];
            if (layout.Positions.TryGetValue(item.FullPath, out var saved))
                item.SetPosition(Clamp(saved, width, height));
            else
            {
                var column = index / rows;
                var row = index % rows;
                item.SetPosition(new DesktopHostPosition(
                    Math.Min(column * ItemWidth, Math.Max(0, width - ItemWidth)),
                    Math.Min(row * ItemHeight, Math.Max(0, height - ItemHeight))));
            }
        }
        return ordered;
    }

    public IReadOnlyList<DesktopHostItem> ApplyOrder(IEnumerable<DesktopHostItem> items) => ApplyLayout(items, 1920, 1080);

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
        var entries = items.Take(MaximumItems).ToArray();
        var layout = new PersistedLayout
        {
            Order = entries.Select(item => item.FullPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Positions = entries.Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
                .GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => new PersistedPosition(group.First().Left, group.First().Top), StringComparer.OrdinalIgnoreCase)
        };
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
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && double.IsFinite(pair.Value.Left) && double.IsFinite(pair.Value.Top))
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

    private sealed class PersistedLayout
    {
        public List<string> Order { get; set; } = [];
        public Dictionary<string, PersistedPosition> Positions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PersistedPosition(double Left, double Top);
}
