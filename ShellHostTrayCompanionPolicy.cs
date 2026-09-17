using System.Globalization;
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
}
