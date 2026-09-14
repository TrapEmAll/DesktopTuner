using System.IO;

namespace DesktopTuner;

public static class ExplorerContentSearch
{
    public const long MaximumFileBytes = 16 * 1024 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".c", ".cfg", ".cmd", ".conf", ".config", ".cpp", ".cs", ".css", ".csv", ".h", ".hpp",
        ".htm", ".html", ".ini", ".java", ".js", ".json", ".log", ".md", ".markdown", ".ps1", ".py",
        ".resx", ".sln", ".sql", ".svg", ".toml", ".ts", ".tsv", ".txt", ".xml", ".xaml", ".yaml", ".yml"
    };

    public static bool CanSearch(string path, long? length) =>
        TextExtensions.Contains(Path.GetExtension(path)) && length is >= 0 and <= MaximumFileBytes;

    public static bool ContainsAll(string path, IReadOnlyList<string> terms, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.Count == 0) return true;
        if (terms.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Content search terms cannot be empty.", nameof(terms));

        var unmatched = new HashSet<int>(Enumerable.Range(0, terms.Count));
        var overlapLength = Math.Max(0, terms.Max(term => term.Length) - 1);
        var buffer = new char[4096];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buffer.Length, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var overlap = string.Empty;

        while (unmatched.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            if (read == 0) break;
            var chunk = overlap + new string(buffer, 0, read);
            foreach (var index in unmatched.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (chunk.Contains(terms[index], StringComparison.OrdinalIgnoreCase)) unmatched.Remove(index);
            }
            if (unmatched.Count == 0) return true;
            overlap = overlapLength == 0 ? string.Empty : chunk[^Math.Min(overlapLength, chunk.Length)..];
        }

        return false;
    }
}
