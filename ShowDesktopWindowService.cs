using System.Runtime.InteropServices;

namespace DesktopTuner;

public sealed class ShowDesktopWindowService
{
    private const uint GW_OWNER = 4;
    private const uint DWMWA_CLOAKED = 14;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private readonly List<nint> _minimizedWindows = [];
    private readonly List<nint> _windowsMinimizedByShortcut = [];
    private bool _desktopIsShown;

    public bool IsDesktopShown => _desktopIsShown;

    public void MinimizeAllWindows(IEnumerable<nint> shellSurfaceHandles)
    {
        foreach (var window in MinimizeEligibleWindows(shellSurfaceHandles))
            if (!_windowsMinimizedByShortcut.Contains(window)) _windowsMinimizedByShortcut.Add(window);
    }

    public void RestoreMinimizedWindows()
    {
        RestoreWindows(_windowsMinimizedByShortcut);
    }

    public void Toggle(IEnumerable<nint> shellSurfaceHandles)
    {
        if (_desktopIsShown)
        {
            RestoreWindows();
            return;
        }

        _minimizedWindows.Clear();
        _minimizedWindows.AddRange(MinimizeEligibleWindows(shellSurfaceHandles));
        _desktopIsShown = true;
    }

    private static List<nint> MinimizeEligibleWindows(IEnumerable<nint> shellSurfaceHandles)
    {
        var shellSurfaces = shellSurfaceHandles.Where(handle => handle != 0).ToHashSet();
        var candidates = new List<ShowDesktopWindowCandidate>();
        EnumWindows((window, _) =>
        {
            var handle = (nint)window;
            var cloaked = DwmGetWindowAttribute(handle, DWMWA_CLOAKED, out var cloakValue, sizeof(int)) == 0 && cloakValue != 0;
            candidates.Add(new ShowDesktopWindowCandidate(
                handle,
                IsWindowVisible(handle),
                IsIconic(handle),
                GetWindow(handle, GW_OWNER) != 0,
                shellSurfaces.Contains(handle),
                cloaked));
            return true;
        }, 0);

        var minimized = new List<nint>();
        foreach (var window in ShowDesktopWindowPolicy.SelectWindowsToMinimize(candidates))
        {
            ShowWindow(window, SW_MINIMIZE);
            if (IsIconic(window)) minimized.Add(window);
        }
        return minimized;
    }

    private void RestoreWindows()
    {
        RestoreWindows(_minimizedWindows);
        _desktopIsShown = false;
    }

    private static void RestoreWindows(List<nint> windows)
    {
        foreach (var window in windows)
            if (IsWindow(window) && IsIconic(window)) ShowWindow(window, SW_RESTORE);
        windows.Clear();
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int valueSize);
}
