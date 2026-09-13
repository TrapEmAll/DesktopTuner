using System.IO;
using System.IO.Enumeration;

namespace DesktopTuner;

public sealed record ExplorerSearchQuery(
    IReadOnlyList<string> NameTerms,
    IReadOnlyList<string> NamePatterns,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> Kinds)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> KindExtensions = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["document"] = new HashSet<string>([".doc", ".docx", ".odt", ".pdf", ".rtf", ".txt", ".xls", ".xlsx", ".ppt", ".pptx", ".csv"], StringComparer.OrdinalIgnoreCase),
        ["picture"] = new HashSet<string>([".bmp", ".gif", ".heic", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"], StringComparer.OrdinalIgnoreCase),
        ["music"] = new HashSet<string>([".aac", ".flac", ".m4a", ".mp3", ".ogg", ".wav", ".wma"], StringComparer.OrdinalIgnoreCase),
        ["video"] = new HashSet<string>([".avi", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".webm", ".wmv"], StringComparer.OrdinalIgnoreCase),
        ["program"] = new HashSet<string>([".bat", ".cmd", ".exe", ".lnk", ".msi", ".ps1"], StringComparer.OrdinalIgnoreCase)
    };

    public static ExplorerSearchQuery Parse(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var terms = new List<string>();
        var patterns = new List<string>();
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kinds = new List<string>();

        foreach (var token in Tokenize(query.Trim()))
        {
            if (TryReadFilter(token, "ext", out var extensionValue) || TryReadFilter(token, "extension", out extensionValue))
            {
                var parsedExtensions = extensionValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.StartsWith('.') ? value : $".{value}")
                    .Where(value => value.Length > 1 && value.Skip(1).All(char.IsLetterOrDigit))
                    .ToArray();
                if (parsedExtensions.Length > 0)
                {
                    foreach (var extension in parsedExtensions) extensions.Add(extension);
                    continue;
                }
            }

            if (TryReadFilter(token, "kind", out var kindValue) && TryNormalizeKind(kindValue, out var kind))
            {
                kinds.Add(kind);
                continue;
            }

            var nameToken = TryReadFilter(token, "name", out var nameValue) ? nameValue : token;
            if (nameToken.Length == 0) continue;
            if (nameToken.Contains('*') || nameToken.Contains('?')) patterns.Add(nameToken);
            else terms.Add(nameToken);
        }

        if (terms.Count == 0 && patterns.Count == 0 && extensions.Count == 0 && kinds.Count == 0)
            throw new ArgumentException("Enter a name or a supported filter such as ext:pdf or kind:folder.", nameof(query));

        return new ExplorerSearchQuery(terms, patterns, extensions.ToArray(), kinds);
    }

    public bool Matches(ExplorerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (NameTerms.Any(term => !entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase))) return false;
        if (NamePatterns.Any(pattern => !FileSystemName.MatchesSimpleExpression(pattern, entry.Name, ignoreCase: true))) return false;
        if (Extensions.Count > 0 && (entry.IsDirectory || !Extensions.Contains(Path.GetExtension(entry.Name)))) return false;
        return Kinds.All(kind => MatchesKind(kind, entry));
    }

    private static bool MatchesKind(string kind, ExplorerEntry entry)
    {
        if (kind == "folder") return entry.IsDirectory;
        if (entry.IsDirectory) return false;
        if (kind == "file") return true;
        return KindExtensions[kind].Contains(Path.GetExtension(entry.Name));
    }

    private static bool TryNormalizeKind(string value, out string kind)
    {
        kind = value.Trim().ToLowerInvariant() switch
        {
            "folder" or "folders" or "directory" or "directories" => "folder",
            "file" or "files" => "file",
            "document" or "documents" => "document",
            "picture" or "pictures" or "photo" or "photos" or "image" or "images" => "picture",
            "music" or "audio" => "music",
            "video" or "videos" => "video",
            "program" or "programs" or "application" or "applications" => "program",
            _ => string.Empty
        };
        return kind.Length > 0;
    }

    private static bool TryReadFilter(string token, string name, out string value)
    {
        var prefix = $"{name}:";
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = token[prefix.Length..];
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static IEnumerable<string> Tokenize(string query)
    {
        var token = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in query)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
                continue;
            }
            token.Append(character);
        }
        if (token.Length > 0) yield return token.ToString();
    }
}
