using System.Globalization;
using Microsoft.Win32;

namespace DesktopTuner;

public static class TaskbarClockPolicy
{
    private const string ExplorerAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ShowSecondsValue = "ShowSecondsInSystemClock";

    public static string FormatTime(DateTime value, CultureInfo? culture = null, bool showSeconds = false) =>
        value.ToString(showSeconds ? "T" : "t", culture ?? CultureInfo.CurrentCulture);

    public static bool ShouldShowSeconds()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKey);
            return Convert.ToInt32(key?.GetValue(ShowSecondsValue, 0), CultureInfo.InvariantCulture) == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string FormatDate(DateTime value, CultureInfo? culture = null) =>
        value.ToString("d", culture ?? CultureInfo.CurrentCulture);
}
