using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DesktopTuner;

public sealed class RunCommandHistoryStore
{
    public const int MaximumEntries = 12;
    public const int MaximumCommandLength = 4096;

    private readonly string _path;

    public RunCommandHistoryStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopTuner", "run-command-history.json"));
    }

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            return Normalize(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)));
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning($"Could not read Run command history from '{_path}': {ex.Message}");
            return [];
        }
        catch (IOException ex)
        {
            Trace.TraceWarning($"Could not read Run command history from '{_path}': {ex.Message}");
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning($"Could not read Run command history from '{_path}': {ex.Message}");
            return [];
        }
    }

    public bool TryRecord(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine) || commandLine.Length > MaximumCommandLength) return false;
        var normalized = new[] { commandLine.Trim() }
            .Concat(Load().Where(existing => !string.Equals(existing, commandLine.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Take(MaximumEntries)
            .ToArray();
        return TrySave(normalized);
    }

    public bool TryClear() => TrySave([]);

    private bool TrySave(IReadOnlyList<string> commands)
    {
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Normalize(commands)));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (IOException ex)
        {
            Trace.TraceWarning($"Could not save Run command history to '{_path}': {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning($"Could not save Run command history to '{_path}': {ex.Message}");
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException ex) { Trace.TraceWarning($"Could not remove temporary Run history file '{temporaryPath}': {ex.Message}"); }
                catch (UnauthorizedAccessException ex) { Trace.TraceWarning($"Could not remove temporary Run history file '{temporaryPath}': {ex.Message}"); }
            }
        }
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string>? commands) => (commands ?? [])
        .Where(command => !string.IsNullOrWhiteSpace(command) && command.Length <= MaximumCommandLength)
        .Select(command => command.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaximumEntries)
        .ToList();
}
