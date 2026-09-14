using System.Diagnostics;
using System.IO;

namespace DesktopTuner;

public sealed record ControlPanelApplet(string Id, string Label, IReadOnlyList<string> Arguments);
public sealed record ControlPanelAppletPreferences(List<string>? Order = null, List<string>? Visible = null);

public static class ControlPanelAppletCatalog
{
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
        new("ease-of-access", "Ease of Access Center", ["access.cpl"])
    ];

    public static IReadOnlyList<ControlPanelApplet> GetAvailableApplets(
        string systemDirectory,
        Func<string, bool>? fileExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemDirectory);
        fileExists ??= File.Exists;
        return Applets.Where(applet =>
        {
            var target = applet.Arguments.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(target) || target.StartsWith("/", StringComparison.Ordinal) ||
                !target.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase)) return true;
            return fileExists(Path.Combine(systemDirectory, Path.GetFileName(target)));
        }).ToArray();
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
