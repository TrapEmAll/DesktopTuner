using System.Diagnostics;
using System.IO;

namespace DesktopTuner;

public sealed record AppEntry(string Name, string ShortcutPath);

public sealed class AppCatalogService
{
    public IReadOnlyList<AppEntry> FindStartMenuApps(string? query = null)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        var entries = new List<AppEntry>();
        foreach (var root in roots)
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.Equals("Uninstall", StringComparison.OrdinalIgnoreCase) || name.Equals("Help", StringComparison.OrdinalIgnoreCase)) continue;
                    entries.Add(new AppEntry(name, path));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
        }

        return Search(entries, query);
    }

    public static IReadOnlyList<AppEntry> Search(IEnumerable<AppEntry> entries, string? query = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var matches = entries
            .DistinctBy(entry => entry.ShortcutPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim();
            var terms = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            matches = matches.Where(entry => terms.All(term => entry.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(entry => entry.Name.StartsWith(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        return matches.ToList();
    }

    public static void Launch(AppEntry entry) => Process.Start(new ProcessStartInfo(entry.ShortcutPath) { UseShellExecute = true });

    public static void OpenLocation(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
