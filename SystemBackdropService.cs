using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopTuner;

public static class SystemBackdropService
{
    private const int SystemBackdropTypeAttribute = 38;
    private const int WindowCornerPreferenceAttribute = 33;
    private const int MainWindowBackdrop = 2;
    private const int TransientWindowBackdrop = 3;
    private const int SmallRoundedCorners = 3;

    public static bool TryApplyMica(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var originalBackground = window.Background;
        window.Background = Brushes.Transparent;

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                var result = SetSystemBackdrop(handle, MainWindowBackdrop);
                if (result == 0) return true;
                Trace.TraceInformation($"DWM Mica backdrop is unavailable for '{window.Title}' (HRESULT 0x{result:X8}); keeping the solid theme background.");
            }
        }
        catch (DllNotFoundException ex)
        {
            Trace.TraceInformation($"DWM is unavailable for '{window.Title}': {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            Trace.TraceInformation($"DWM backdrop support is unavailable for '{window.Title}': {ex.Message}");
        }

        window.Background = originalBackground;
        return false;
    }

    public static bool TryApplyTransientBackdrop(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;
        try
        {
            var result = SetSystemBackdrop(windowHandle, TransientWindowBackdrop);
            if (result == 0) return true;
            Trace.TraceInformation($"DWM acrylic backdrop is unavailable for menu window (HRESULT 0x{result:X8}); keeping the solid menu background.");
        }
        catch (DllNotFoundException ex)
        {
            Trace.TraceInformation($"DWM is unavailable for a menu window: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            Trace.TraceInformation($"DWM transient backdrops are unavailable: {ex.Message}");
        }
        return false;
    }

    public static bool TryApplyRoundedMenuCorners(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;
        try
        {
            var preference = SmallRoundedCorners;
            var result = DwmSetWindowAttribute(windowHandle, WindowCornerPreferenceAttribute, ref preference, sizeof(int));
            if (result == 0) return true;
            Trace.TraceInformation($"DWM rounded menu corners are unavailable (HRESULT 0x{result:X8}).");
        }
        catch (DllNotFoundException ex)
        {
            Trace.TraceInformation($"DWM is unavailable for rounded menu corners: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            Trace.TraceInformation($"DWM rounded menu corners are unavailable: {ex.Message}");
        }
        return false;
    }

    private static int SetSystemBackdrop(IntPtr window, int backdropType) =>
        DwmSetWindowAttribute(window, SystemBackdropTypeAttribute, ref backdropType, sizeof(int));

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
