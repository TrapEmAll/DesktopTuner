using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopTuner;

public static class SystemBackdropService
{
    private const int ImmersiveDarkModeAttribute = 20;
    private const int SystemBackdropTypeAttribute = 38;
    private const int WindowCornerPreferenceAttribute = 33;
    private const int MainWindowBackdrop = 2;
    private const int TransientWindowBackdrop = 3;
    private const int NoSystemBackdrop = 1;
    private const int SmallRoundedCorners = 3;

    public static bool TryApplyMica(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        TryApplySystemDarkMode(window);
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

    public static bool TryApplySystemDarkMode(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;

        try
        {
            var useDarkMode = TaskbarTheme.ReadSystemDarkMode() ? 1 : 0;
            var result = DwmSetWindowAttribute(handle, ImmersiveDarkModeAttribute, ref useDarkMode, sizeof(int));
            if (result == 0) return true;
            Trace.TraceInformation($"DWM system dark mode is unavailable for '{window.Title}' (HRESULT 0x{result:X8}).");
        }
        catch (DllNotFoundException ex)
        {
            Trace.TraceInformation($"DWM is unavailable for '{window.Title}': {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            Trace.TraceInformation($"DWM dark mode is unavailable for '{window.Title}': {ex.Message}");
        }
        return false;
    }

    public static void RefreshOpenWindowDarkMode()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || Application.Current is not { } application) return;
        foreach (Window window in application.Windows)
        {
            if (PresentationSource.FromVisual(window) is HwndSource)
                TryApplySystemDarkMode(window);
        }
    }

    public static bool TryApplyTransientBackdrop(IntPtr windowHandle)
        => TrySetBackdrop(windowHandle, TransientWindowBackdrop, "DWM acrylic backdrop", "a transient window");

    public static bool TryClearSystemBackdrop(IntPtr windowHandle)
        => TrySetBackdrop(windowHandle, NoSystemBackdrop, "clearing the DWM backdrop", "a window");

    private static bool TrySetBackdrop(IntPtr windowHandle, int backdropType, string operation, string description)
    {
        if (windowHandle == IntPtr.Zero) return false;
        try
        {
            var result = SetSystemBackdrop(windowHandle, backdropType);
            if (result == 0) return true;
            Trace.TraceInformation($"{operation} is unavailable for {description} (HRESULT 0x{result:X8}); keeping its themed background.");
        }
        catch (DllNotFoundException ex)
        {
            Trace.TraceInformation($"DWM is unavailable for {description}: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            Trace.TraceInformation($"DWM backdrop support is unavailable for {description}: {ex.Message}");
        }
        return false;
    }

    public static bool TryApplySmallRoundedCorners(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;
        try
        {
            var preference = SmallRoundedCorners;
            var result = DwmSetWindowAttribute(windowHandle, WindowCornerPreferenceAttribute, ref preference, sizeof(int));
            if (result == 0) return true;
            Trace.TraceInformation($"DWM small rounded corners are unavailable (HRESULT 0x{result:X8}).");
        }
        catch (DllNotFoundException ex)
        {
            Trace.TraceInformation($"DWM is unavailable for small rounded corners: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            Trace.TraceInformation($"DWM small rounded corners are unavailable: {ex.Message}");
        }
        return false;
    }

    private static int SetSystemBackdrop(IntPtr window, int backdropType) =>
        DwmSetWindowAttribute(window, SystemBackdropTypeAttribute, ref backdropType, sizeof(int));

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
