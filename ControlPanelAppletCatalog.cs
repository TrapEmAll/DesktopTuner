using System.Diagnostics;

namespace DesktopTuner;

public sealed record ControlPanelApplet(string Id, string Label, IReadOnlyList<string> Arguments);

public static class ControlPanelAppletCatalog
{
    public static IReadOnlyList<ControlPanelApplet> Applets { get; } =
    [
        new("programs", "Programs and Features", ["appwiz.cpl"]),
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

    public static ProcessStartInfo CreateStartInfo(string id)
    {
        var applet = Applets.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown Control Panel applet.");
        var startInfo = new ProcessStartInfo("control.exe") { UseShellExecute = true };
        foreach (var argument in applet.Arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }
}
