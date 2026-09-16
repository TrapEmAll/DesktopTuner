using Microsoft.Win32;
using System.IO;

namespace DesktopTuner;

public sealed record ShellNewItem(string Extension, string Label, string? TemplatePath, bool UsesNullFile)
{
    public string CreateName() => $"New {Label}{Extension}";
}

public static class ShellNewItemCatalog
{
    public const int MaximumItems = 24;

    public static IReadOnlyList<ShellNewItem> Read()
    {
        var entries = new List<ShellNewItem>();
        try
        {
            using var classes = Registry.ClassesRoot;
            foreach (var extension in classes.GetSubKeyNames()
                         .Where(name => name.StartsWith(".", StringComparison.Ordinal) && name.Length > 1)
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (entries.Count >= MaximumItems) break;
                using var extensionKey = classes.OpenSubKey(extension);
                var className = extensionKey?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(className)) continue;
                using var shellNew = extensionKey?.OpenSubKey("ShellNew");
                if (shellNew is null) continue;

                var nullFile = shellNew.GetValue("NullFile") is not null;
                var template = shellNew.GetValue("FileName") as string;
                if (!nullFile && string.IsNullOrWhiteSpace(template)) continue;
                if (!nullFile && shellNew.GetValue("Data") is not null) continue;

                var label = classes.OpenSubKey(className)?.GetValue(null) as string;
                label = NormalizeLabel(label, extension);
                entries.Add(new ShellNewItem(extension, label, ExpandTemplatePath(template), nullFile));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }

        return entries
            .GroupBy(item => item.Extension, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeLabel(string? label, string extension)
    {
        if (string.IsNullOrWhiteSpace(label)) return $"{extension.TrimStart('.').ToUpperInvariant()} file";
        var value = label.Trim();
        if (value.StartsWith('@')) value = value[1..];
        var comma = value.IndexOf(',');
        if (comma > 0) value = value[..comma];
        return string.IsNullOrWhiteSpace(value) ? $"{extension.TrimStart('.').ToUpperInvariant()} file" : value;
    }

    private static string? ExpandTemplatePath(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        var value = Environment.ExpandEnvironmentVariables(template.Trim());
        return Path.IsPathRooted(value) ? value : null;
    }
}
