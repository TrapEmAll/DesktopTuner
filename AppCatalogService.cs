using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Microsoft.CSharp.RuntimeBinder;
using System.Windows.Media;

namespace DesktopTuner;

public sealed record AppEntry(string Name, string ShortcutPath, bool IsPackagedApp = false, string CategoryPath = "", StartTileSize TileSize = StartTileSize.Medium, string GroupName = StartPinCatalog.DefaultGroupName, bool IsDirectory = false, bool IsShellNamespace = false)
{
    public string SourceDescription => IsShellNamespace ? "Shell location" : IsPackagedApp ? "Windows app" : IsDirectory ? ShortcutPath : Path.GetDirectoryName(ShortcutPath) ?? ShortcutPath;
    [JsonIgnore]
    public ImageSource? Icon => IsShellNamespace ? TaskbarIconService.LoadNamespaceIcon(ShortcutPath) : TaskbarIconService.LoadIcon(ShortcutPath);
    [JsonIgnore]
    public bool CanRunElevated => AppCatalogService.CanRunAsAdministrator(this);
    [JsonIgnore]
    public bool CanOpenFileLocation => AppCatalogService.CanOpenFileLocation(this);
    [JsonIgnore]
    public bool CanPinToTaskbar => IsPackagedApp
        ? TaskbarPinCatalog.IsSupportedPackagedTarget(ShortcutPath)
        : IsShellNamespace ? TaskbarPinCatalog.IsSupportedShellNamespaceTarget(ShortcutPath)
        : TaskbarPinCatalog.IsSupportedTarget(ShortcutPath, IsDirectory) && (IsDirectory ? Directory.Exists(ShortcutPath) : CanOpenFileLocation);
}

public sealed class StartMenuNode(string name, AppEntry? application = null)
{
    public string Name { get; } = name;
    public AppEntry? Application { get; } = application;
    public bool CanPinApplication => StartPinCatalog.IsSupported(Application);
    public bool CanRunApplicationAsAdministrator => Application?.CanRunElevated == true;
    public bool CanOpenApplicationFileLocation => Application?.CanOpenFileLocation == true;
    public bool CanPinApplicationToTaskbar => Application?.CanPinToTaskbar == true;
    public List<StartMenuNode> Children { get; } = [];
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
                    var directory = Path.GetDirectoryName(path)!;
                    var category = Path.GetRelativePath(root, directory);
                    entries.Add(new AppEntry(name, path, CategoryPath: category == "." ? string.Empty : category));
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
        var uniqueEntries = entries.DistinctBy(entry => entry.ShortcutPath, StringComparer.OrdinalIgnoreCase).ToList();
        if (string.IsNullOrWhiteSpace(query))
            return uniqueEntries.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var normalizedQuery = query.Trim();
        var terms = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return uniqueEntries
            .Select(entry => (Entry: entry, Rank: SearchRank(entry, normalizedQuery, terms)))
            .Where(match => match.Rank >= 0)
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(match => match.Entry)
            .ToList();
    }

    private static int SearchRank(AppEntry entry, string query, IReadOnlyList<string> terms)
    {
        if (string.Equals(entry.Name, query, StringComparison.CurrentCultureIgnoreCase)) return 0;
        if (entry.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 1;
        if (terms.All(term => StartsAtWordBoundary(entry.Name, term))) return 2;
        if (entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 3;

        var category = entry.CategoryPath ?? string.Empty;
        if (terms.All(term => entry.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains(term, StringComparison.CurrentCultureIgnoreCase))) return 4;

        var searchableWords = GetSearchWords(entry.Name + " " + category);
        return terms.All(term => searchableWords.Any(word => IsFuzzyWordMatch(term, word))) ? 5 : -1;
    }

    private static string[] GetSearchWords(string value) => value
        .Split([' ', '\\', '/', '-', '_', '.', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsFuzzyWordMatch(string query, string word)
    {
        if (query.Length < 4 || word.Length < 4) return false;
        var maximumDistance = query.Length >= 10 ? 2 : 1;
        if (Math.Abs(query.Length - word.Length) > maximumDistance) return false;

        var previousPrevious = new int[word.Length + 1];
        var previous = Enumerable.Range(0, word.Length + 1).ToArray();
        var current = new int[word.Length + 1];
        for (var queryIndex = 1; queryIndex <= query.Length; queryIndex++)
        {
            current[0] = queryIndex;
            var rowMinimum = current[0];
            for (var wordIndex = 1; wordIndex <= word.Length; wordIndex++)
            {
                var substitutionCost = char.ToUpperInvariant(query[queryIndex - 1]) == char.ToUpperInvariant(word[wordIndex - 1]) ? 0 : 1;
                current[wordIndex] = Math.Min(
                    Math.Min(previous[wordIndex] + 1, current[wordIndex - 1] + 1),
                    previous[wordIndex - 1] + substitutionCost);
                if (queryIndex > 1 && wordIndex > 1
                    && char.ToUpperInvariant(query[queryIndex - 1]) == char.ToUpperInvariant(word[wordIndex - 2])
                    && char.ToUpperInvariant(query[queryIndex - 2]) == char.ToUpperInvariant(word[wordIndex - 1]))
                    current[wordIndex] = Math.Min(current[wordIndex], previousPrevious[wordIndex - 2] + 1);
                rowMinimum = Math.Min(rowMinimum, current[wordIndex]);
            }

            if (rowMinimum > maximumDistance) return false;
            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[word.Length] <= maximumDistance;
    }

    private static bool StartsAtWordBoundary(string value, string term)
    {
        for (var index = 0; index <= value.Length - term.Length; index++)
        {
            var isBoundary = index == 0 || !char.IsLetterOrDigit(value[index - 1]) ||
                (char.IsUpper(value[index]) && char.IsLower(value[index - 1]));
            if (isBoundary && string.Compare(value, index, term, 0, term.Length, StringComparison.CurrentCultureIgnoreCase) == 0)
                return true;
        }
        return false;
    }

    public static IReadOnlyList<StartMenuNode> BuildTree(IEnumerable<AppEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var roots = new List<StartMenuNode>();
        foreach (var entry in entries)
        {
            var categoryPath = string.IsNullOrWhiteSpace(entry.CategoryPath) && entry.IsPackagedApp
                ? "Windows apps"
                : entry.CategoryPath;
            var categories = categoryPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            var currentLevel = roots;
            foreach (var category in categories)
            {
                var node = currentLevel.FirstOrDefault(candidate => candidate.Application is null && string.Equals(candidate.Name, category, StringComparison.OrdinalIgnoreCase));
                if (node is null)
                {
                    node = new StartMenuNode(category);
                    currentLevel.Add(node);
                }
                currentLevel = node.Children;
            }
            currentLevel.Add(new StartMenuNode(entry.Name, entry));
        }

        SortTree(roots);
        return roots;
    }

    private static void SortTree(List<StartMenuNode> nodes)
    {
        nodes.Sort((left, right) =>
        {
            var typeOrder = (left.Application is null ? 0 : 1).CompareTo(right.Application is null ? 0 : 1);
            return typeOrder != 0 ? typeOrder : StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
        });
        foreach (var node in nodes) SortTree(node.Children);
    }

    public static void Launch(AppEntry entry)
    {
        if (entry.IsShellNamespace)
        {
            var shellLaunch = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            shellLaunch.ArgumentList.Add(entry.ShortcutPath);
            Process.Start(shellLaunch);
            return;
        }
        if (entry.IsDirectory)
        {
            Process.Start(new ProcessStartInfo(entry.ShortcutPath) { UseShellExecute = true });
            return;
        }

        if (!entry.IsPackagedApp)
        {
            Process.Start(BuildLaunchInfo(entry));
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add($"shell:AppsFolder\\{entry.ShortcutPath}");
        Process.Start(startInfo);
    }

    public static bool CanRunAsAdministrator(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsPackagedApp || entry.IsDirectory || entry.IsShellNamespace) return false;
        var extension = Path.GetExtension(entry.ShortcutPath);
        return string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanOpenFileLocation(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return !entry.IsPackagedApp && !entry.IsDirectory && !entry.IsShellNamespace
            && (string.Equals(Path.GetExtension(entry.ShortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(entry.ShortcutPath), ".exe", StringComparison.OrdinalIgnoreCase))
            && File.Exists(entry.ShortcutPath);
    }

    public static ProcessStartInfo BuildFileLocationLaunchInfo(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanOpenFileLocation(entry))
            throw new NotSupportedException("This Start menu entry does not have an available file location.");

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add($"/select,\"{entry.ShortcutPath}\"");
        return startInfo;
    }

    public static void OpenFileLocation(AppEntry entry) => Process.Start(BuildFileLocationLaunchInfo(entry));

    public static ProcessStartInfo BuildPathLocationLaunchInfo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new NotSupportedException("The requested Shell item is no longer available.");

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add(Directory.Exists(fullPath) ? fullPath : $"/select,\"{fullPath}\"");
        return startInfo;
    }

    public static void OpenPathLocation(string path) => Process.Start(BuildPathLocationLaunchInfo(path));

    public static ProcessStartInfo BuildLaunchInfo(AppEntry entry, bool runAsAdministrator = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (runAsAdministrator && !CanRunAsAdministrator(entry))
            throw new NotSupportedException("This app entry cannot be started with administrator privileges.");
        return new ProcessStartInfo(entry.ShortcutPath)
        {
            UseShellExecute = true,
            Verb = runAsAdministrator ? "runas" : string.Empty
        };
    }

    public static void LaunchAsAdministrator(AppEntry entry) => Process.Start(BuildLaunchInfo(entry, runAsAdministrator: true));

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
                        entries.Add(new AppEntry(name, applicationId, IsPackagedApp: true, CategoryPath: "Windows apps"));
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
