using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTuner;

public enum DesktopWallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5
}

public sealed record DesktopWallpaperMonitor(string DeviceName, string? WallpaperPath, double ScaleX, double ScaleY);

public sealed record DesktopWallpaperSnapshot(
    DesktopWallpaperPosition Position,
    Color BackgroundColor,
    IReadOnlyList<DesktopWallpaperMonitor> Monitors);

public sealed record DesktopWallpaperPresentation(Stretch Stretch, bool Tile, bool Span);

public static class DesktopWallpaperPresentationPolicy
{
    public static TaskbarDisplay? FindDisplay(IEnumerable<TaskbarDisplay> displays, int left, int top, int right, int bottom)
    {
        ArgumentNullException.ThrowIfNull(displays);
        return displays.FirstOrDefault(display => display.Left == left && display.Top == top &&
            display.Width == right - left && display.Height == bottom - top);
    }

    public static DesktopWallpaperPresentation Resolve(DesktopWallpaperPosition position) => position switch
    {
        DesktopWallpaperPosition.Center => new(Stretch.None, false, false),
        DesktopWallpaperPosition.Tile => new(Stretch.None, true, false),
        DesktopWallpaperPosition.Stretch => new(Stretch.Fill, false, false),
        DesktopWallpaperPosition.Fit => new(Stretch.Uniform, false, false),
        DesktopWallpaperPosition.Fill => new(Stretch.UniformToFill, false, false),
        DesktopWallpaperPosition.Span => new(Stretch.Fill, false, true),
        _ => new(Stretch.UniformToFill, false, false)
    };
}

public static class DesktopWallpaperService
{
    private const uint SpiGetDesktopWallpaper = 0x0073;
    private const int MaximumPath = 260;
    private const string DesktopWallpaperClassId = "C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD";

    public static DesktopWallpaperSnapshot? LoadCurrent(IEnumerable<TaskbarDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var connectedDisplays = displays.ToArray();
        if (connectedDisplays.Length == 0) return null;

        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            if (wallpaper.GetMonitorDevicePathCount(out var monitorCount) < 0 || monitorCount == 0)
                return null;

            var rawPosition = (int)DesktopWallpaperPosition.Fill;
            if (wallpaper.GetPosition(out var position) >= 0) rawPosition = (int)position;
            uint rawColor = 0;
            wallpaper.GetBackgroundColor(out rawColor);
            var monitorWallpapers = new List<DesktopWallpaperMonitor>();
            for (uint monitorIndex = 0; monitorIndex < monitorCount; monitorIndex++)
            {
                if (wallpaper.GetMonitorDevicePathAt(monitorIndex, out var monitorIdPointer) < 0 || monitorIdPointer == nint.Zero)
                    continue;

                try
                {
                    var monitorId = Marshal.PtrToStringUni(monitorIdPointer);
                    if (string.IsNullOrWhiteSpace(monitorId) || wallpaper.GetMonitorRect(monitorId, out var bounds) < 0)
                        continue;

                    var display = DesktopWallpaperPresentationPolicy.FindDisplay(
                        connectedDisplays, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                    if (display is null) continue;

                    string? wallpaperPath = null;
                    if (wallpaper.GetWallpaper(monitorId, out var wallpaperPathPointer) >= 0 && wallpaperPathPointer != nint.Zero)
                    {
                        try { wallpaperPath = Marshal.PtrToStringUni(wallpaperPathPointer); }
                        finally { Marshal.FreeCoTaskMem(wallpaperPathPointer); }
                    }

                    monitorWallpapers.Add(new DesktopWallpaperMonitor(
                        display.DeviceName,
                        !string.IsNullOrWhiteSpace(wallpaperPath) && File.Exists(wallpaperPath) ? wallpaperPath : null,
                        display.ScaleX,
                        display.ScaleY));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(monitorIdPointer);
                }
            }

            if (monitorWallpapers.Count == 0) return null;
            return new DesktopWallpaperSnapshot(
                Enum.IsDefined(typeof(DesktopWallpaperPosition), rawPosition)
                    ? (DesktopWallpaperPosition)rawPosition
                    : DesktopWallpaperPosition.Fill,
                Color.FromRgb((byte)rawColor, (byte)(rawColor >> 8), (byte)(rawColor >> 16)),
                monitorWallpapers);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException or DllNotFoundException or EntryPointNotFoundException)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not read Windows desktop wallpaper settings: {ex.Message}");
            return null;
        }
        finally
        {
            if (wallpaper is not null && Marshal.IsComObject(wallpaper))
                Marshal.ReleaseComObject(wallpaper);
        }
    }

    public static BitmapImage? LoadPrimaryWallpaper()
    {
        var path = new StringBuilder(MaximumPath);
        if (!SystemParametersInfo(SpiGetDesktopWallpaper, (uint)MaximumPath, path, 0)) return null;
        return LoadImage(path.ToString());
    }

    public static BitmapImage? LoadImage(string? wallpaperPath)
    {
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
            System.Diagnostics.Trace.TraceWarning($"Could not load desktop wallpaper '{wallpaperPath}': {ex.Message}");
            return null;
        }
    }

    [ComImport]
    [Guid(DesktopWallpaperClassId)]
    [ClassInterface(ClassInterfaceType.None)]
    private class DesktopWallpaperClass { }

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out nint wallpaper);
        [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, out nint monitorId);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRect([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out NativeRect displayRect);
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int GetBackgroundColor(out uint color);
        [PreserveSig] int SetPosition(DesktopWallpaperPosition position);
        [PreserveSig] int GetPosition(out DesktopWallpaperPosition position);
        [PreserveSig] int SetSlideshow(nint items);
        [PreserveSig] int GetSlideshow(out nint items);
        [PreserveSig] int SetSlideshowOptions(uint options, uint slideshowTick);
        [PreserveSig] int GetSlideshowOptions(out uint options, out uint slideshowTick);
        [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, uint direction);
        [PreserveSig] int GetStatus(out uint state);
        [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, StringBuilder value, uint flags);
}
