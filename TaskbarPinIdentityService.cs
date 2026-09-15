using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace DesktopTuner;

public static class TaskbarPinIdentityService
{
    private static readonly ConcurrentDictionary<string, string> ShortcutTargetCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool Matches(PinnedTaskbarApp app, RunningWindow window) => Matches(app, window, ResolveShortcutTarget);

    public static bool Matches(PinnedTaskbarApp app, RunningWindow window, Func<string, string?> shortcutTargetResolver)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(shortcutTargetResolver);
        if (app.IsPackagedApp)
            return string.Equals(TaskbarJumpListService.GetAppUserModelId(window), app.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        if (app.IsDirectory || string.IsNullOrWhiteSpace(app.ExecutablePath) || string.IsNullOrWhiteSpace(window.ExecutablePath)) return false;

        var pinnedTarget = string.Equals(Path.GetExtension(app.ExecutablePath), ".lnk", StringComparison.OrdinalIgnoreCase)
            ? shortcutTargetResolver(app.ExecutablePath) ?? app.ExecutablePath
            : app.ExecutablePath;
        return string.Equals(NormalizePath(pinnedTarget), NormalizePath(window.ExecutablePath), StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveShortcutTarget(string shortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        var normalizedPath = NormalizePath(shortcutPath);
        return ShortcutTargetCache.GetOrAdd(normalizedPath, _ => ReadShortcutTarget(shortcutPath) ?? shortcutPath);
    }

    private static string? ReadShortcutTarget(string shortcutPath)
    {
        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return null;

            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            if (shortcutObject is null) return null;
            dynamic shortcut = shortcutObject;
            var targetPath = (string)shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(targetPath) ? null : targetPath;
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidCastException)
        {
            Trace.TraceWarning($"Could not resolve taskbar shortcut '{shortcutPath}': {ex.Message}");
            return null;
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
