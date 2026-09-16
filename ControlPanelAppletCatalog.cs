using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public sealed record ControlPanelApplet(string Id, string Label, IReadOnlyList<string> Arguments);
public sealed record ControlPanelAppletPreferences(List<string>? Order = null, List<string>? Visible = null);

public static class ControlPanelAppletCatalog
{
    private static readonly object CanonicalProbeLock = new();
    private static IReadOnlySet<string> _canonicalApplicationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static DateTime _canonicalProbeExpiresUtc;

    public static IReadOnlyList<ControlPanelApplet> Applets { get; } =
    [
        new("programs", "Programs and Features", ["appwiz.cpl"]),
        new("bluetooth", "Bluetooth Devices", ["bthprops.cpl"]),
        new("hardware", "Add Hardware", ["hdwwiz.cpl"]),
        new("infrared", "Infrared", ["irprops.cpl"]),
        new("game-controllers", "Game Controllers", ["joy.cpl"]),
        new("tablet-pc", "Tablet PC Settings", ["TabletPC.cpl"]),
        new("phone-modem", "Phone and Modem", ["telephon.cpl"]),
        new("power", "Power Options", ["powercfg.cpl"]),
        new("network", "Network Connections", ["ncpa.cpl"]),
        new("sound", "Sound", ["mmsys.cpl"]),
        new("date-time", "Date and Time", ["timedate.cpl"]),
        new("region", "Region", ["intl.cpl"]),
        new("mouse", "Mouse", ["main.cpl"]),
        new("keyboard", "Keyboard", ["main.cpl", "keyboard"]),
        new("system", "System Properties", ["sysdm.cpl"]),
        new("display", "Display", ["desk.cpl"]),
        new("fonts", "Fonts", ["fonts"]),
        new("file-explorer-options", "File Explorer Options", ["folders"]),
        new("personalization", "Personalization", ["/name", "Microsoft.Personalization"]),
        new("devices-printers", "Devices and Printers", ["/name", "Microsoft.DevicesAndPrinters"]),
        new("user-accounts", "User Accounts", ["/name", "Microsoft.UserAccounts"]),
        new("firewall", "Windows Defender Firewall", ["firewall.cpl"]),
        new("network-sharing", "Network and Sharing Center", ["/name", "Microsoft.NetworkAndSharingCenter"]),
        new("internet-options", "Internet Options", ["inetcpl.cpl"]),
        new("credential-manager", "Credential Manager", ["/name", "Microsoft.CredentialManager"]),
        new("security-maintenance", "Security and Maintenance", ["wscui.cpl"]),
        new("ease-of-access", "Ease of Access Center", ["access.cpl"]),
        new("mobility-center", "Mobility Center", ["/name", "Microsoft.MobilityCenter"]),
        new("recovery", "Recovery", ["/name", "Microsoft.Recovery"]),
        new("work-folders", "Work Folders", ["/name", "Microsoft.WorkFolders"]),
        new("default-programs", "Default Programs", ["/name", "Microsoft.DefaultPrograms"]),
        new("remote-apps", "RemoteApp and Desktop Connections", ["/name", "Microsoft.RemoteAppAndDesktopConnections"]),
        new("indexing-options", "Indexing Options", ["/name", "Microsoft.IndexingOptions"]),
        new("autoplay", "AutoPlay", ["/name", "Microsoft.AutoPlay"]),
        new("sync-center", "Sync Center", ["/name", "Microsoft.SyncCenter"]),
        new("color-management", "Color Management", ["/name", "Microsoft.ColorManagement"]),
        new("backup-restore", "Backup and Restore (Windows 7)", ["/name", "Microsoft.BackupAndRestore"]),
        new("windows-tools", "Windows Tools", ["/name", "Microsoft.AdministrativeTools"]),
        new("troubleshooting", "Troubleshooting", ["/name", "Microsoft.Troubleshooting"]),
        new("file-history", "File History", ["/name", "Microsoft.FileHistory"]),
        new("storage-spaces", "Storage Spaces", ["/name", "Microsoft.StorageSpaces"]),
        new("device-manager", "Device Manager", ["/name", "Microsoft.DeviceManager"]),
        new("taskbar-navigation", "Taskbar and Navigation", ["/name", "Microsoft.Taskbar"])
    ];

    public static IReadOnlyList<ControlPanelApplet> GetAvailableApplets(
        string systemDirectory,
        Func<string, bool>? fileExists = null,
        IReadOnlySet<string>? canonicalApplicationNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemDirectory);
        fileExists ??= File.Exists;
        return Applets.Where(applet =>
        {
            var target = applet.Arguments.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(target)) return false;
            if (target.StartsWith("/", StringComparison.Ordinal))
                return canonicalApplicationNames is null || canonicalApplicationNames.Count == 0 ||
                    applet.Arguments.Skip(1).Any(canonicalApplicationNames.Contains);
            if (!target.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase)) return true;
            return fileExists(Path.Combine(systemDirectory, Path.GetFileName(target)));
        }).ToArray();
    }

    public static IReadOnlySet<string> DiscoverCanonicalApplicationNames()
    {
        lock (CanonicalProbeLock)
        {
            if (DateTime.UtcNow < _canonicalProbeExpiresUtc)
                return new HashSet<string>(_canonicalApplicationNames, StringComparer.OrdinalIgnoreCase);
        }

        var names = ProbeCanonicalApplicationNames();
        lock (CanonicalProbeLock)
        {
            _canonicalApplicationNames = names;
            _canonicalProbeExpiresUtc = DateTime.UtcNow.AddSeconds(30);
            return new HashSet<string>(_canonicalApplicationNames, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static HashSet<string> ProbeCanonicalApplicationNames()
    {
        object? shell = null;
        object? folder = null;
        object? items = null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
            if (shellType is null) return names;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return names;
            dynamic shellDispatch = shell;
            folder = shellDispatch.Namespace("shell:ControlPanelFolder");
            if (folder is null) return names;
            dynamic folderDispatch = folder;
            items = folderDispatch.Items();
            if (items is null) return names;
            foreach (dynamic item in (dynamic)items)
            {
                object? itemObject = item;
                try
                {
                    dynamic shellItem = itemObject!;
                    var rawName = Convert.ToString((object?)shellItem.ExtendedProperty("System.ApplicationName"));
                    if (string.IsNullOrWhiteSpace(rawName)) continue;
                    var name = rawName.Split('\0')[0].Trim();
                    if (name.Length > 0) names.Add(name);
                }
                catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or FormatException)
                {
                    Trace.TraceWarning($"Could not read a Control Panel canonical application name: {ex.Message}");
                }
                finally
                {
                    if (itemObject is not null && Marshal.IsComObject(itemObject)) Marshal.ReleaseComObject(itemObject);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or FormatException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.TraceWarning($"Could not probe Control Panel canonical applications: {ex.Message}");
            names.Clear();
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }
        return names;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    public static ControlPanelAppletPreferences Normalize(ControlPanelAppletPreferences? preferences)
    {
        var validIds = Applets.Select(applet => applet.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requestedOrder = preferences?.Order ?? [];
        var order = requestedOrder.Where(validIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Concat(Applets.Select(applet => applet.Id).Where(id => !requestedOrder.Contains(id, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        var visibleSet = preferences?.Visible is null
            ? order.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : preferences.Visible.Where(validIds.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (preferences?.Visible is not null)
            foreach (var id in Applets.Select(applet => applet.Id).Where(id => !requestedOrder.Contains(id, StringComparer.OrdinalIgnoreCase)))
                visibleSet.Add(id);
        return new ControlPanelAppletPreferences(order, order.Where(visibleSet.Contains).ToList());
    }

    public static ControlPanelAppletPreferences Move(ControlPanelAppletPreferences? preferences, string appletId, int offset)
    {
        var normalized = Normalize(preferences);
        if (offset is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(offset));
        var index = normalized.Order!.FindIndex(id => string.Equals(id, appletId, StringComparison.OrdinalIgnoreCase));
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= normalized.Order.Count) return normalized;
        (normalized.Order[index], normalized.Order[destination]) = (normalized.Order[destination], normalized.Order[index]);
        return Normalize(normalized);
    }

    public static ProcessStartInfo CreateStartInfo(string id)
    {
        var applet = Applets.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown Control Panel applet.");
        var startInfo = new ProcessStartInfo("control.exe") { UseShellExecute = true };
        foreach (var argument in applet.Arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }
}
