using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace DesktopTuner;

public static class ShellHostTrayCompanionPolicy
{
    public const string RegistryPath = @"Software\DesktopTuner";
    public const string ValueName = "ShellHostTrayCompanion";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return Convert.ToInt32(key?.GetValue(ValueName, 0), CultureInfo.InvariantCulture) == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new IOException("Could not open Desktop Tuner settings for the Explorer tray companion.");
        key.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }
}
