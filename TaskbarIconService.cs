using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTuner;

public static class TaskbarIconService
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_PIDL = 0x000000008;
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> NamespaceIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Color?> PrimaryColorCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static ImageSource? LoadIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
            var icon = LoadIconCore(path);
            Cache.Add(path, icon);
            return icon;
        }
    }

    public static ImageSource? LoadNamespaceIcon(string parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return null;
        lock (CacheLock)
        {
            if (NamespaceIconCache.TryGetValue(parsingName, out var cached)) return cached;
            var icon = LoadNamespaceIconCore(parsingName);
            NamespaceIconCache.Add(parsingName, icon);
            return icon;
        }
    }

    public static Color? GetPrimaryColor(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (CacheLock)
        {
            if (PrimaryColorCache.TryGetValue(path, out var cached)) return cached;
            var color = TaskbarAuraColorPolicy.ResolvePrimaryColor(LoadIcon(path));
            PrimaryColorCache.Add(path, color);
            return color;
        }
    }

    private static ImageSource? LoadIconCore(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return LoadPackagedAppIcon(path);

        var info = new ShellFileInfo();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), SHGFI_ICON | SHGFI_LARGEICON) == 0 || info.Icon == IntPtr.Zero)
            return null;

        return CreateImageSource(info.Icon);
    }

    private static ImageSource? LoadNamespaceIconCore(string parsingName)
    {
        IntPtr itemIdList = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(parsingName, IntPtr.Zero, out itemIdList, 0, out _) < 0 || itemIdList == IntPtr.Zero)
                return null;

            var info = new ShellFileInfo();
            if (SHGetFileInfo(itemIdList, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON) == 0 || info.Icon == IntPtr.Zero)
                return null;

            return CreateImageSource(info.Icon);
        }
        finally
        {
            if (itemIdList != IntPtr.Zero) Marshal.FreeCoTaskMem(itemIdList);
        }
    }

    private static ImageSource? LoadPackagedAppIcon(string applicationId)
    {
        if (!applicationId.Contains('!')) return null;

        IntPtr itemIdList = IntPtr.Zero;
        try
        {
            var parsingName = $"shell:AppsFolder\\{applicationId}";
            if (SHParseDisplayName(parsingName, IntPtr.Zero, out itemIdList, 0, out _) < 0 || itemIdList == IntPtr.Zero)
                return null;

            var info = new ShellFileInfo();
            if (SHGetFileInfo(itemIdList, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON) == 0 || info.Icon == IntPtr.Zero)
                return null;

            return CreateImageSource(info.Icon);
        }
        finally
        {
            if (itemIdList != IntPtr.Zero) Marshal.FreeCoTaskMem(itemIdList);
        }
    }

    private static ImageSource CreateImageSource(IntPtr icon)
    {
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            bitmap.Freeze();
            return bitmap;
        }
        finally { DestroyIcon(icon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string? TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShellFileInfo fileInfo, uint fileInfoSize, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(IntPtr itemIdList, uint fileAttributes, ref ShellFileInfo fileInfo, uint fileInfoSize, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr itemIdList, uint attributes, out uint attributesOut);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
