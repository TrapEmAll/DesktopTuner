using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public sealed class ForegroundWindowHistory : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int MaximumTrackedWindows = 512;
    private readonly object _sync = new();
    private readonly List<nint> _mostRecentFirst = [];
    private readonly WinEventProc _callback;
    private nint _hook;

    public ForegroundWindowHistory()
    {
        _callback = OnForegroundChanged;
        var orderedWindows = new List<nint>();
        EnumWindows((window, _) =>
        {
            if (window != 0) orderedWindows.Add(window);
            return true;
        }, 0);
        lock (_sync) _mostRecentFirst.AddRange(orderedWindows.Distinct());

        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_hook == 0)
            Trace.TraceWarning($"Could not track foreground window history (Windows error {Marshal.GetLastWin32Error()}); the initial z-order snapshot remains available.");
        Record(GetForegroundWindow());
    }

    public IReadOnlyList<nint> GetMostRecentFirst()
    {
        lock (_sync) return _mostRecentFirst.ToArray();
    }

    private void OnForegroundChanged(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND) Record(window);
    }

    private void Record(nint window)
    {
        if (window == 0) return;
        lock (_sync)
        {
            _mostRecentFirst.Remove(window);
            _mostRecentFirst.Insert(0, window);
            if (_mostRecentFirst.Count > MaximumTrackedWindows)
                _mostRecentFirst.RemoveRange(MaximumTrackedWindows, _mostRecentFirst.Count - MaximumTrackedWindows);
        }
    }

    public void Dispose()
    {
        if (_hook == 0) return;
        UnhookWinEvent(_hook);
        _hook = 0;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);
    private delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
}
