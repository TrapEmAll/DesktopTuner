using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesktopTuner;

/// <summary>
/// Hosts Explorer's notification-area child window for the explicitly opted-in shell tray companion.
/// This is isolated from normal overlay mode because SetParent crosses process boundaries.
/// </summary>
public sealed class NativeTrayHost : HwndHost
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const nint WsChild = 0x40000000;
    private const nint WsVisible = 0x10000000;
    private static readonly nint WsPopup = unchecked((nint)0x80000000);
    private const nint WsExAppWindow = 0x00040000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private nint _hostHandle;
    private nint _trayHandle;
    private nint _originalParent;
    private nint _originalStyle;
    private nint _originalExStyle;
    private NativeRect _originalRect;

    public string WindowClassName { get; set; } = "TrayNotifyWnd";

    public bool TryAttach(TaskbarDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (_hostHandle == nint.Zero) return false;
        var candidate = NativeTaskbarTrayService.FindTrayCandidate(display, WindowClassName);
        if (candidate is not { } selected || selected.TrayWindow == nint.Zero || !IsWindow(selected.TrayWindow))
        {
            Detach();
            return false;
        }

        if (_trayHandle != selected.TrayWindow)
        {
            Detach();
            if (!TryReparent(selected.TrayWindow)) return false;
        }

        var pixelWidth = Math.Max(1, (int)Math.Round(selected.Bounds.Width));
        var pixelHeight = Math.Max(1, (int)Math.Round(selected.Bounds.Height));
        Width = pixelWidth / Math.Max(1, display.ScaleX);
        Height = pixelHeight / Math.Max(1, display.ScaleY);
        if (!SetWindowPos(_trayHandle, nint.Zero, 0, 0, pixelWidth, pixelHeight, SwpNoActivate | SwpNoZOrder | SwpShowWindow))
        {
            Detach();
            return false;
        }
        Visibility = System.Windows.Visibility.Visible;
        return true;
    }

    public void Detach()
    {
        if (_trayHandle == nint.Zero) return;
        var tray = _trayHandle;
        _trayHandle = nint.Zero;
        try
        {
            if (IsWindow(tray))
            {
                SetParent(tray, _originalParent);
                SetWindowLongPtr(tray, GwlStyle, _originalStyle);
                SetWindowLongPtr(tray, GwlExStyle, _originalExStyle);
                SetWindowPos(tray, nint.Zero, _originalRect.Left, _originalRect.Top,
                    _originalRect.Right - _originalRect.Left, _originalRect.Bottom - _originalRect.Top,
                    SwpNoActivate | SwpNoZOrder | SwpShowWindow);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not restore the Explorer notification area parent: {ex.Message}");
        }
        Visibility = System.Windows.Visibility.Collapsed;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostHandle = CreateWindowEx(0, "STATIC", string.Empty, WsChild | WsVisible, 0, 0, 1, 1,
            hwndParent.Handle, nint.Zero, GetModuleHandle(null), nint.Zero);
        if (_hostHandle == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create the native tray host.");
        return new HandleRef(this, _hostHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Detach();
        if (_hostHandle != nint.Zero)
        {
            DestroyWindow(_hostHandle);
            _hostHandle = nint.Zero;
        }
    }

    private bool TryReparent(nint tray)
    {
        if (!GetWindowRect(tray, out _originalRect)) return false;
        _originalParent = GetParent(tray);
        if (_originalParent != nint.Zero)
        {
            var origin = new NativePoint { X = _originalRect.Left, Y = _originalRect.Top };
            if (ScreenToClient(_originalParent, ref origin))
            {
                var width = _originalRect.Right - _originalRect.Left;
                var height = _originalRect.Bottom - _originalRect.Top;
                _originalRect = new NativeRect { Left = origin.X, Top = origin.Y, Right = origin.X + width, Bottom = origin.Y + height };
            }
        }
        _originalStyle = GetWindowLongPtr(tray, GwlStyle);
        _originalExStyle = GetWindowLongPtr(tray, GwlExStyle);
        SetLastError(0);
        if (SetParent(tray, _hostHandle) == nint.Zero && Marshal.GetLastWin32Error() != 0)
        {
            System.Diagnostics.Trace.TraceWarning("Windows rejected hosting the Explorer notification area in the replacement taskbar.");
            return false;
        }

        if (!TrySetWindowLongPtr(tray, GwlStyle, (_originalStyle | WsChild | WsVisible) & ~WsPopup)
            || !TrySetWindowLongPtr(tray, GwlExStyle, _originalExStyle & ~WsExAppWindow))
        {
            SetParent(tray, _originalParent);
            SetWindowLongPtr(tray, GwlStyle, _originalStyle);
            SetWindowLongPtr(tray, GwlExStyle, _originalExStyle);
            return false;
        }
        _trayHandle = tray;
        return true;
    }

    private static bool TrySetWindowLongPtr(nint window, int index, nint value)
    {
        SetLastError(0);
        SetWindowLongPtr(window, index, value);
        return Marshal.GetLastWin32Error() == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string? windowName, nint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern void SetLastError(uint errorCode);
}
