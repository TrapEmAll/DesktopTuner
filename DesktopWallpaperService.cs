using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;

namespace DesktopTuner;

public static class DesktopWallpaperService
{
    private const uint SpiGetDesktopWallpaper = 0x0073;
    private const int MaximumPath = 260;

    public static BitmapImage? LoadPrimaryWallpaper()
    {
        var path = new StringBuilder(MaximumPath);
        if (!SystemParametersInfo(SpiGetDesktopWallpaper, (uint)MaximumPath, path, 0)) return null;
        var wallpaperPath = path.ToString();
        if (string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(wallpaperPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, StringBuilder value, uint flags);
}
