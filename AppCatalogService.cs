using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace DesktopTuner;

public sealed record AppEntry(string Name, string ShortcutPath, bool IsPackagedApp = false)
{
    public string SourceDescription => IsPackagedApp ? "Windows app" : Path.GetDirectoryName(ShortcutPath) ?? ShortcutPath;
}

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
        entries.AddRange(FindPackagedApps());

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

    public static void Launch(AppEntry entry)
    {
        if (!entry.IsPackagedApp)
        {
            Process.Start(new ProcessStartInfo(entry.ShortcutPath) { UseShellExecute = true });
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add($"shell:AppsFolder\\{entry.ShortcutPath}");
        Process.Start(startInfo);
    }

    private static IReadOnlyList<AppEntry> FindPackagedApps()
    {
        var entries = new List<AppEntry>();
        object? shellObject = null;
        object? folderObject = null;
        object? itemsObject = null;
        object? currentItem = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return entries;
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return entries;
            dynamic shell = shellObject;
            folderObject = shell.Namespace("shell:AppsFolder");
            if (folderObject is null) return entries;
            dynamic folder = folderObject;
            itemsObject = folder.Items();
            if (itemsObject is null) return entries;

            foreach (var item in (System.Collections.IEnumerable)itemsObject)
            {
                currentItem = item;
                try
                {
                    dynamic shellItem = currentItem;
                    var name = (string)shellItem.Name;
                    var applicationId = (string)shellItem.Path;
                    if (!string.IsNullOrWhiteSpace(name) && applicationId.Contains('!'))
                        entries.Add(new AppEntry(name, applicationId, IsPackagedApp: true));
                }
                finally
                {
                    ReleaseComObject(currentItem);
                    currentItem = null;
                }
            }
        }
        catch (COMException) { }
        catch (RuntimeBinderException) { }
        catch (InvalidCastException) { }
        finally
        {
            ReleaseComObject(currentItem);
            ReleaseComObject(itemsObject);
            ReleaseComObject(folderObject);
            ReleaseComObject(shellObject);
        }
        return entries;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    public static void OpenLocation(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
