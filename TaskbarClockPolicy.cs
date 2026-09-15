using System.Globalization;
using Microsoft.Win32;

namespace DesktopTuner;

public static class TaskbarClockPolicy
{
    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001a;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
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

    public static void SetShowSeconds(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ExplorerAdvancedKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Explorer taskbar settings.");
        key.SetValue(ShowSecondsValue, enabled ? 1 : 0, RegistryValueKind.DWord);
        SendMessageTimeout(new IntPtr(HWND_BROADCAST), WM_SETTINGCHANGE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out _);
    }

    public static string FormatDate(DateTime value, CultureInfo? culture = null) =>
        value.ToString("d", culture ?? CultureInfo.CurrentCulture);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
