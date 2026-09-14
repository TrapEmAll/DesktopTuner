using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTuner;

internal sealed class VirtualDesktopWindowService : IDisposable
{
    private static readonly Guid ClassId = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
    private static int _warningLogged;
    private readonly object _instance;
    private readonly IVirtualDesktopManager _manager;

    private VirtualDesktopWindowService(object instance)
    {
        _instance = instance;
        _manager = (IVirtualDesktopManager)instance;
    }

    public static VirtualDesktopWindowService? TryCreate()
    {
        object? instance = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ClassId, throwOnError: true)!;
            instance = Activator.CreateInstance(type) ?? throw new COMException("Windows did not create the virtual desktop manager.");
            return new VirtualDesktopWindowService(instance);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or PlatformNotSupportedException or ArgumentException)
        {
            Release(instance);
            WarnOnce($"Could not query Windows virtual desktops; taskbar windows will remain visible. {ex.Message}");
            return null;
        }
    }

    public bool? IsWindowOnCurrentDesktop(nint window)
    {
        try
        {
            var result = _manager.IsWindowOnCurrentVirtualDesktop(window, out var onCurrentDesktop);
            if (result >= 0) return onCurrentDesktop;
            Marshal.ThrowExceptionForHR(result);
        }
        catch (COMException ex)
        {
            WarnOnce($"Could not determine whether a taskbar window belongs to the current virtual desktop; that window will remain visible. {ex.Message}");
        }
        return null;
    }

    public void Dispose() => Release(_instance);

    private static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
    }

    private static void WarnOnce(string message)
    {
        if (Interlocked.Exchange(ref _warningLogged, 1) == 0) Trace.TraceWarning(message);
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(nint topLevelWindow, in Guid desktopId);
    }
}
