using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public sealed class DesktopHostLayoutStore
{
    public const int MaximumItems = 4096;
    private readonly string _path;

    public DesktopHostLayoutStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopTuner", "desktop-host-layout.json");
    }

    public IReadOnlyList<DesktopHostItem> ApplyOrder(IEnumerable<DesktopHostItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var remaining = items.DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.FullPath, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DesktopHostItem>(remaining.Count);
        foreach (var key in ReadOrder())
            if (remaining.Remove(key, out var item)) ordered.Add(item);
        ordered.AddRange(remaining.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase));
        return ordered;
    }

    public bool SaveOrder(IEnumerable<DesktopHostItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var order = items.Select(item => item.FullPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumItems)
            .ToArray();
        var temporaryPath = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? Environment.CurrentDirectory);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(order, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.TraceWarning($"Could not save desktop icon order to '{_path}': {ex.Message}");
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

    private IReadOnlyList<string> ReadOrder()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path));
            return (values ?? []).Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumItems).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.TraceWarning($"Could not read desktop icon order from '{_path}': {ex.Message}");
            return [];
        }
    }
}
