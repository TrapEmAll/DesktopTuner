using System.Globalization;

namespace DesktopTuner;

public static class TaskbarClockPolicy
{
    public static string FormatTime(DateTime value, CultureInfo? culture = null) =>
        value.ToString("t", culture ?? CultureInfo.CurrentCulture);

    public static string FormatDate(DateTime value, CultureInfo? culture = null) =>
        value.ToString("d", culture ?? CultureInfo.CurrentCulture);
}
