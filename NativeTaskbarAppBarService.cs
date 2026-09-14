using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public sealed class NativeTaskbarAppBarService : IDisposable
{
    public const int CallbackMessage = 0x8000 + 0x2A5;
    public const int PositionChangedNotification = 1;
    public const int FullscreenAppNotification = 2;
    public const int WindowArrangeNotification = 3;

    private const uint AppBarNew = 0x00000000;
    private const uint AppBarRemove = 0x00000001;
    private const uint AppBarQueryPosition = 0x00000002;
    private const uint AppBarSetPosition = 0x00000003;
    private const uint AppBarActivate = 0x00000006;
    private const uint AppBarWindowPositionChanged = 0x00000009;
    private const uint AppBarSetAutoHideBarEx = 0x0000000C;
    private static readonly nint HwndBottom = (nint)1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint AppBarEdgeLeft = 0;
    private const uint AppBarEdgeRight = 1;
    private const uint AppBarEdgeTop = 2;
    private const uint AppBarEdgeBottom = 3;

    private nint _window;
    private TaskbarDisplay? _display;
    private TaskbarEdge _edge;
    private TaskbarBounds? _lastPosition;
    private bool _autoHideRegistered;
    private TaskbarEdge _autoHideRegisteredEdge;
    private TaskbarBounds? _autoHideRegisteredDisplayBounds;
    private bool _positioning;

    public bool IsRegistered { get; private set; }

    public bool Register(nint window, TaskbarDisplay display, TaskbarEdge edge)
    {
        if (window == nint.Zero) throw new ArgumentException("A taskbar window handle is required.", nameof(window));
        ArgumentNullException.ThrowIfNull(display);
        if (IsRegistered && _window == window
            && string.Equals(_display?.DeviceName, display.DeviceName, StringComparison.OrdinalIgnoreCase)
            && _edge == edge)
        {
            _display = display;
            return true;
        }

        Unregister();
        var data = NewData(window);
        data.CallbackMessage = unchecked((uint)CallbackMessage);
        if (SHAppBarMessage(AppBarNew, ref data) == UIntPtr.Zero)
        {
            Trace.TraceWarning($"Windows did not register taskbar window {window} as an appbar.");
            return false;
        }

        _window = window;
        _display = display;
        _edge = edge;
        _lastPosition = null;
        IsRegistered = true;
        return true;
    }

    public TaskbarBounds? UpdatePosition(TaskbarBounds desiredBounds)
    {
        ArgumentNullException.ThrowIfNull(desiredBounds);
        if (!IsRegistered || _display is null) return null;
        if (_positioning) return _lastPosition;

        _positioning = true;
        try
        {
            var proposal = TaskbarAppBarPolicy.ProposeBounds(_display, _edge, desiredBounds);
            var data = NewData(_window);
            data.Edge = ToNativeEdge(_edge);
            data.Bounds = ToNativeRect(proposal);
            if (SHAppBarMessage(AppBarQueryPosition, ref data) == UIntPtr.Zero)
                throw new InvalidOperationException("Windows did not return an appbar position.");

            var queriedBounds = ToTaskbarBounds(data.Bounds);
            var approvedBounds = TaskbarAppBarPolicy.PreserveThickness(_edge, queriedBounds, proposal);
            if (_lastPosition is not { } last || !SameBounds(last, approvedBounds))
            {
                data.Bounds = ToNativeRect(approvedBounds);
                if (SHAppBarMessage(AppBarSetPosition, ref data) == UIntPtr.Zero)
                    throw new InvalidOperationException("Windows did not accept the appbar position.");
                approvedBounds = ToTaskbarBounds(data.Bounds);
                _lastPosition = approvedBounds;
            }

            return _lastPosition;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            Trace.TraceWarning($"Could not position the replacement taskbar as an appbar: {ex.Message}");
            return null;
        }
        finally
        {
            _positioning = false;
        }
    }

    public void NotifyActivated() => NotifyWindowMessage(AppBarActivate);

    public void NotifyWindowPositionChanged() => NotifyWindowMessage(AppBarWindowPositionChanged);

    public bool SetAutoHideRegistration(bool enabled)
    {
        if (!IsRegistered || _display is null) return false;
        var displayBounds = new TaskbarBounds(_display.Left, _display.Top, _display.Width, _display.Height);
        var registrationMatches = _autoHideRegistered
            && _autoHideRegisteredEdge == _edge
            && _autoHideRegisteredDisplayBounds is { } registeredBounds
            && SameBounds(registeredBounds, displayBounds);
        if (enabled && registrationMatches) return true;
        if (!enabled && !_autoHideRegistered) return true;

        if (_autoHideRegistered)
        {
            if (_autoHideRegisteredDisplayBounds is not { } oldBounds
                || !SendAutoHideRegistration(false, _autoHideRegisteredEdge, oldBounds))
                return false;
            _autoHideRegistered = false;
            _autoHideRegisteredDisplayBounds = null;
        }

        if (!enabled) return true;
        if (!SendAutoHideRegistration(true, _edge, displayBounds))
        {
            Trace.TraceWarning($"Windows refused auto-hide registration for taskbar window {_window} on {_display.DeviceName}/{_edge}; the taskbar's local auto-hide behavior remains active.");
            return false;
        }

        _autoHideRegistered = true;
        _autoHideRegisteredEdge = _edge;
        _autoHideRegisteredDisplayBounds = displayBounds;
        return true;
    }

    public void LowerBelowFullscreenWindows()
    {
        if (!IsRegistered) return;
        if (!SetWindowPos(_window, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder))
            Trace.TraceWarning($"Could not lower replacement taskbar window {_window} below a full-screen app.");
    }

    public void Unregister()
    {
        if (!IsRegistered) return;
        try
        {
            if (_autoHideRegistered) SetAutoHideRegistration(false);
            var data = NewData(_window);
            SHAppBarMessage(AppBarRemove, ref data);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Trace.TraceWarning($"Could not unregister the replacement taskbar appbar: {ex.Message}");
        }
        finally
        {
            IsRegistered = false;
            _window = nint.Zero;
            _display = null;
            _lastPosition = null;
            _autoHideRegistered = false;
            _autoHideRegisteredDisplayBounds = null;
            _positioning = false;
        }
    }

    public void Dispose() => Unregister();

    private void NotifyWindowMessage(uint message)
    {
        if (!IsRegistered) return;
        try
        {
            var data = NewData(_window);
            SHAppBarMessage(message, ref data);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Trace.TraceWarning($"Could not notify Windows about replacement taskbar window state: {ex.Message}");
        }
    }

    private bool SendAutoHideRegistration(bool enabled, TaskbarEdge edge, TaskbarBounds displayBounds)
    {
        try
        {
            var data = NewData(_window);
            data.Edge = ToNativeEdge(edge);
            data.Bounds = ToNativeRect(displayBounds);
            data.Parameter = enabled ? 1 : 0;
            var succeeded = SHAppBarMessage(AppBarSetAutoHideBarEx, ref data) != UIntPtr.Zero;
            if (!succeeded && !enabled)
                Trace.TraceWarning($"Windows refused to release auto-hide registration for taskbar window {_window} on {edge}.");
            return succeeded;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Trace.TraceWarning($"Could not update replacement taskbar auto-hide registration: {ex.Message}");
            return false;
        }
    }

    private static bool SameBounds(TaskbarBounds left, TaskbarBounds right) =>
        left.Left == right.Left && left.Top == right.Top && left.Width == right.Width && left.Height == right.Height;

    private static uint ToNativeEdge(TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Left => AppBarEdgeLeft,
        TaskbarEdge.Right => AppBarEdgeRight,
        TaskbarEdge.Top => AppBarEdgeTop,
        TaskbarEdge.Bottom => AppBarEdgeBottom,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown taskbar edge.")
    };

    private static NativeRect ToNativeRect(TaskbarBounds bounds) => new()
    {
        Left = checked((int)Math.Round(bounds.Left)),
        Top = checked((int)Math.Round(bounds.Top)),
        Right = checked((int)Math.Round(bounds.Left + bounds.Width)),
        Bottom = checked((int)Math.Round(bounds.Top + bounds.Height))
    };

    private static TaskbarBounds ToTaskbarBounds(NativeRect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static AppBarData NewData(nint window) => new()
    {
        Size = (uint)Marshal.SizeOf<AppBarData>(),
        Window = window
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public nint Window;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Bounds;
        public int Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
