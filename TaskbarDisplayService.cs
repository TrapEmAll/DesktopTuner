using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DesktopTuner;

public sealed record TaskbarDisplay(
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary,
    double ScaleX = 1,
    double ScaleY = 1);

public sealed record TaskbarDisplayTopologyPlan(
    IReadOnlyList<TaskbarDisplay> Retained,
    IReadOnlyList<TaskbarDisplay> Added,
    IReadOnlyList<string> RemovedDeviceNames);

public static class TaskbarDisplayService
{
    private const uint MonitorInfoPrimary = 0x00000001;

    public static IReadOnlyList<TaskbarDisplay> Enumerate()
    {
        var displays = new List<TaskbarDisplay>();
        Exception? enumerationError = null;
        MonitorEnumProc callback = (IntPtr monitor, IntPtr deviceContext, ref NativeRect monitorRect, IntPtr data) =>
        {
            try
            {
                var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
                if (!GetMonitorInfo(monitor, ref info))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read display bounds.");

                displays.Add(new TaskbarDisplay(
                    info.DeviceName,
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top,
                    (info.Flags & MonitorInfoPrimary) != 0));
                return true;
            }
            catch (Exception ex)
            {
                enumerationError = ex;
                return false;
            }
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            if (enumerationError is not null) throw new InvalidOperationException("Could not enumerate desktop displays.", enumerationError);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate desktop displays.");
        }
        if (displays.Count == 0) throw new InvalidOperationException("Windows did not report any desktop displays.");
        return displays.OrderByDescending(display => display.IsPrimary).ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<TaskbarDisplay> Select(bool allDisplays)
    {
        return Select(Enumerate(), allDisplays);
    }

    public static IReadOnlyList<TaskbarDisplay> Select(IEnumerable<TaskbarDisplay> connectedDisplays, bool allDisplays)
    {
        ArgumentNullException.ThrowIfNull(connectedDisplays);
        var displays = connectedDisplays.OrderByDescending(display => display.IsPrimary).ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(connectedDisplays));
        if (allDisplays) return displays;
        return [displays.FirstOrDefault(display => display.IsPrimary) ?? displays[0]];
    }

    public static TaskbarDisplayTopologyPlan PlanTopologyChange(IEnumerable<TaskbarDisplay> currentDisplays, IEnumerable<TaskbarDisplay> desiredDisplays)
    {
        ArgumentNullException.ThrowIfNull(currentDisplays);
        ArgumentNullException.ThrowIfNull(desiredDisplays);
        var currentByName = currentDisplays.ToDictionary(display => display.DeviceName, StringComparer.OrdinalIgnoreCase);
        var desired = desiredDisplays
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (desired.Count == 0) throw new ArgumentException("At least one display is required.", nameof(desiredDisplays));
        if (desired.Select(display => display.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != desired.Count)
            throw new ArgumentException("Display device names must be unique.", nameof(desiredDisplays));

        var desiredNames = desired.Select(display => display.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retained = desired.Where(display => currentByName.ContainsKey(display.DeviceName)).ToList();
        var added = desired.Where(display => !currentByName.ContainsKey(display.DeviceName)).ToList();
        var removed = currentByName.Keys.Where(name => !desiredNames.Contains(name)).ToList();
        return new(retained, added, removed);
    }

    public static bool Overlaps(TaskbarBounds bounds, TaskbarDisplay display) =>
        bounds.Width > 0 && bounds.Height > 0 &&
        bounds.Left < display.Left + display.Width && bounds.Left + bounds.Width > display.Left &&
        bounds.Top < display.Top + display.Height && bounds.Top + bounds.Height > display.Top;

    public static bool PositionWindow(Window window, TaskbarBounds bounds)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        return SetWindowPos(handle, IntPtr.Zero,
            (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
            Math.Max(1, (int)Math.Round(bounds.Width)), Math.Max(1, (int)Math.Round(bounds.Height)),
            SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
    }

    public static TaskbarDisplay ReadWindowDpi(TaskbarDisplay display, Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return display;
        var dpi = GetDpiForWindow(handle);
        return dpi == 0 ? display : display with { ScaleX = dpi / 96d, ScaleY = dpi / 96d };
    }

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, ref NativeRect monitorRect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
