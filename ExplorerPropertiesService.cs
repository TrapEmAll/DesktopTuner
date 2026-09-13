using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class ExplorerPropertiesService
{
    private const uint SHOP_FILEPATH = 0x0002;

    public static bool CanShowProperties(ExplorerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return !entry.IsDrive && (File.Exists(entry.FullPath) || Directory.Exists(entry.FullPath));
    }

    public static bool ShowProperties(ExplorerEntry entry, IntPtr parentWindow)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanShowProperties(entry)) return false;

        return SHObjectProperties(parentWindow, SHOP_FILEPATH, Path.GetFullPath(entry.FullPath), null);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string objectName, string? propertyPage);
}
