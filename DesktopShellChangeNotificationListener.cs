using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesktopTuner;

public sealed class DesktopShellChangeNotificationListener : IDisposable
{
    private const int WindowMessage = 0x8000 + 0x4D1;
    private const int DesktopFolderId = 0x0000;
    private const int Sources = 0x0001 | 0x0002 | 0x1000 | 0x8000;
    private const int ShellChangeEvents = 0x7FFFFFFF;
    private readonly HwndSource _windowSource;
    private readonly Action _onChanged;
    private readonly uint _registrationId;
    private bool _disposed;

    public DesktopShellChangeNotificationListener(HwndSource windowSource, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(windowSource);
        ArgumentNullException.ThrowIfNull(onChanged);
        _windowSource = windowSource;
        _onChanged = onChanged;

        _windowSource.AddHook(WindowProcedure);
        try
        {
            _registrationId = Register(_windowSource.Handle);
        }
        catch
        {
            _windowSource.RemoveHook(WindowProcedure);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!SHChangeNotifyDeregister(_registrationId))
            Trace.TraceWarning("Windows could not unregister the desktop Shell change notification listener.");
        _windowSource.RemoveHook(WindowProcedure);
    }

    private uint Register(IntPtr windowHandle)
    {
        var result = SHGetFolderLocation(windowHandle, DesktopFolderId, IntPtr.Zero, 0, out var desktopPidl);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        if (desktopPidl == IntPtr.Zero)
            throw new InvalidOperationException("Windows did not provide a desktop Shell namespace identifier.");

        try
        {
            var entry = new ShellChangeNotifyEntry { Pidl = desktopPidl, Recursive = true };
            var registrationId = SHChangeNotifyRegister(windowHandle, Sources, ShellChangeEvents, WindowMessage, 1, ref entry);
            return registrationId != 0
                ? registrationId
                : throw new InvalidOperationException("Windows could not register the desktop Shell change notification listener.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(desktopPidl);
        }
    }

    private IntPtr WindowProcedure(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WindowMessage) return IntPtr.Zero;
        handled = true;

        var notificationLock = SHChangeNotificationLock(wParam, unchecked((uint)lParam.ToInt64()), out _, out var eventId);
        if (notificationLock == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            if (DesktopHostRefreshPolicy.ShouldRefreshShellEvent(eventId))
            {
                try { _onChanged(); }
                catch (Exception ex) { Trace.TraceError($"Could not queue a desktop refresh after a Shell change notification: {ex}"); }
            }
        }
        finally
        {
            SHChangeNotificationUnlock(notificationLock);
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellChangeNotifyEntry
    {
        public IntPtr Pidl;
        [MarshalAs(UnmanagedType.Bool)] public bool Recursive;
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetFolderLocation(IntPtr owner, int folder, IntPtr token, uint reserved, out IntPtr pidl);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern uint SHChangeNotifyRegister(IntPtr windowHandle, int sources, int events, int message, int entryCount, ref ShellChangeNotifyEntry entries);

    [DllImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHChangeNotifyDeregister(uint registrationId);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr SHChangeNotificationLock(IntPtr changeHandle, uint processId, out IntPtr itemIdLists, out int eventId);

    [DllImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHChangeNotificationUnlock(IntPtr lockHandle);
}
