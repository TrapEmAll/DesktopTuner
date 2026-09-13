using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopTuner;

public static class SystemBackdropService
{
    private const int SystemBackdropTypeAttribute = 38;
    private const int MainWindowBackdrop = 2;

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

    private static int SetSystemBackdrop(IntPtr window, int backdropType) =>
        DwmSetWindowAttribute(window, SystemBackdropTypeAttribute, ref backdropType, sizeof(int));

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
