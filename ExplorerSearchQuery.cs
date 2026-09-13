using System.IO;
using System.IO.Enumeration;
using System.Globalization;

namespace DesktopTuner;

public sealed record ExplorerSearchQuery(
    IReadOnlyList<string> NameTerms,
    IReadOnlyList<string> NamePatterns,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> Kinds)
{
    private enum SizeComparison { Equal, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual }
    private sealed record SizeFilter(SizeComparison Comparison, long Bytes)
    {
        public bool Matches(long size) => Comparison switch
        {
            SizeComparison.Equal => size == Bytes,
            SizeComparison.LessThan => size < Bytes,
            SizeComparison.LessThanOrEqual => size <= Bytes,
            SizeComparison.GreaterThan => size > Bytes,
            SizeComparison.GreaterThanOrEqual => size >= Bytes,
            _ => false
        };
    }

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
        var sizes = new List<SizeFilter>();
        DateOnly? modifiedAfter = null;
        DateOnly? modifiedBefore = null;

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

            if (TryReadFilter(token, "after", out var afterValue))
            {
                if (!DateOnly.TryParseExact(afterValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var afterDate))
                    throw new ArgumentException($"Use after:yyyy-MM-dd for a modified-date filter (received '{afterValue}').", nameof(query));
                modifiedAfter = modifiedAfter is null || afterDate > modifiedAfter ? afterDate : modifiedAfter;
                continue;
            }

            if (TryReadFilter(token, "before", out var beforeValue))
            {
                if (!DateOnly.TryParseExact(beforeValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var beforeDate))
                    throw new ArgumentException($"Use before:yyyy-MM-dd for a modified-date filter (received '{beforeValue}').", nameof(query));
                modifiedBefore = modifiedBefore is null || beforeDate < modifiedBefore ? beforeDate : modifiedBefore;
                continue;
            }

            if (TryReadFilter(token, "size", out var sizeValue))
            {
                if (!TryParseSizeFilter(sizeValue, out var sizeFilter))
                    throw new ArgumentException($"Use size:[comparison]number[unit], such as size:>=10MB (received '{sizeValue}').", nameof(query));
                sizes.Add(sizeFilter);
                continue;
            }

            var nameToken = TryReadFilter(token, "name", out var nameValue) ? nameValue : token;
            if (nameToken.Length == 0) continue;
            if (nameToken.Contains('*') || nameToken.Contains('?')) patterns.Add(nameToken);
            else terms.Add(nameToken);
        }

        if (terms.Count == 0 && patterns.Count == 0 && extensions.Count == 0 && kinds.Count == 0 && sizes.Count == 0 && modifiedAfter is null && modifiedBefore is null)
            throw new ArgumentException("Enter a name or a supported filter such as ext:pdf, kind:folder, after:2026-01-01, or size:>=10MB.", nameof(query));

        return new ExplorerSearchQuery(terms, patterns, extensions.ToArray(), kinds)
        {
            Sizes = sizes,
            ModifiedAfter = modifiedAfter,
            ModifiedBefore = modifiedBefore
        };
    }

    private IReadOnlyList<SizeFilter> Sizes { get; init; } = [];
    private DateOnly? ModifiedAfter { get; init; }
    private DateOnly? ModifiedBefore { get; init; }

    public bool Matches(ExplorerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (NameTerms.Any(term => !entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase))) return false;
        if (NamePatterns.Any(pattern => !FileSystemName.MatchesSimpleExpression(pattern, entry.Name, ignoreCase: true))) return false;
        if (Extensions.Count > 0 && (entry.IsDirectory || !Extensions.Contains(Path.GetExtension(entry.Name)))) return false;
        var modifiedDate = DateOnly.FromDateTime(entry.Modified);
        if (ModifiedAfter is { } after && modifiedDate < after) return false;
        if (ModifiedBefore is { } before && modifiedDate > before) return false;
        if (Sizes.Count > 0 && (entry.IsDirectory || entry.Length is not { } size || Sizes.Any(filter => !filter.Matches(size)))) return false;
        return Kinds.All(kind => MatchesKind(kind, entry));
    }

    private static bool TryParseSizeFilter(string value, out SizeFilter filter)
    {
        filter = new SizeFilter(SizeComparison.Equal, 0);
        var comparison = SizeComparison.Equal;
        var numberStart = 0;
        if (value.StartsWith(">=", StringComparison.Ordinal)) { comparison = SizeComparison.GreaterThanOrEqual; numberStart = 2; }
        else if (value.StartsWith("<=", StringComparison.Ordinal)) { comparison = SizeComparison.LessThanOrEqual; numberStart = 2; }
        else if (value.StartsWith('>')) { comparison = SizeComparison.GreaterThan; numberStart = 1; }
        else if (value.StartsWith('<')) { comparison = SizeComparison.LessThan; numberStart = 1; }
        else if (value.StartsWith('=')) numberStart = 1;

        var sizeText = value[numberStart..].Trim();
        var unitStart = sizeText.TakeWhile(character => char.IsDigit(character) || character == '.' || character == ',').Count();
        if (unitStart == 0
            || !decimal.TryParse(sizeText[..unitStart], NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            || number < 0) return false;

        var unit = sizeText[unitStart..].Trim().ToUpperInvariant();
        var multiplier = unit switch
        {
            "" or "B" => 1m,
            "K" or "KB" or "KIB" => 1024m,
            "M" or "MB" or "MIB" => 1024m * 1024m,
            "G" or "GB" or "GIB" => 1024m * 1024m * 1024m,
            "T" or "TB" or "TIB" => 1024m * 1024m * 1024m * 1024m,
            _ => 0m
        };
        var bytes = number * multiplier;
        if (multiplier == 0 || bytes > long.MaxValue || decimal.Truncate(bytes) != bytes) return false;
        filter = new SizeFilter(comparison, decimal.ToInt64(bytes));
        return true;
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
