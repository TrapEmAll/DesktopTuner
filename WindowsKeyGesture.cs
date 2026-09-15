namespace DesktopTuner;

public enum WindowsKeyAction
{
    PassThrough,
    Suppress,
    ForwardWindowsDownThenPass,
    ForwardWindowsUpThenSuppress,
    OpenStartMenu,
    ActivateTaskbarPin,
    FocusTaskbar,
    FocusTaskbarPrevious,
    FocusTaskbarSystem,
    OpenPowerUserMenu,
    OpenRunDialog,
    OpenExplorer,
    ForwardWindowsTapThenSuppress,
    ToggleDesktop,
    MinimizeAllWindows,
    RestoreMinimizedWindows,
    OpenShellSystemSurface,
    OpenOnScreenKeyboard,
    OpenNarrator,
    LaunchPinnedAppInstance,
    LaunchPinnedAppInstanceAsAdministrator,
    ActivateLastActivePinnedApp
}

public sealed class WindowsKeyGesture
{
    private const uint VK_LWIN = 0x5b;
    private const uint VK_RWIN = 0x5c;
    private const uint VK_SHIFT = 0x10;
    private const uint VK_LSHIFT = 0xa0;
    private const uint VK_RSHIFT = 0xa1;
    private const uint VK_CONTROL = 0x11;
    private const uint VK_LCONTROL = 0xa2;
    private const uint VK_RCONTROL = 0xa3;
    private const uint VK_ESCAPE = 0x1b;
    private uint? _heldWindowsKey;
    private bool _forwarded;
    private bool _taskbarShortcutConsumed;
    private readonly bool _replaceBareWindowsKey;
    private readonly bool _replaceControlEscape;
    private bool _controlEscapeHeld;
    private readonly HashSet<uint> _suppressedShortcutKeys = [];
    public int? TaskbarPinIndex { get; private set; }
    public uint? ShellSystemSurfaceKey { get; private set; }

    public WindowsKeyGesture(bool replaceBareWindowsKey = true, bool replaceControlEscape = false)
    {
        _replaceBareWindowsKey = replaceBareWindowsKey;
        _replaceControlEscape = replaceControlEscape;
    }

    public uint? HeldWindowsKey => _heldWindowsKey;

    public WindowsKeyAction KeyDown(uint key, Func<int, bool>? canActivateTaskbarPin = null, Func<bool>? canFocusTaskbar = null, Func<bool>? canOpenExplorer = null,
        bool controlPressed = false, bool altPressed = false, bool shiftPressed = false, Func<bool>? canToggleDesktop = null,
        Func<bool>? canFocusTaskbarSystem = null, Func<bool>? canOpenPowerUserMenu = null, Func<bool>? canOpenRunDialog = null,
        Func<bool>? canMinimizeAllWindows = null, Func<bool>? canRestoreMinimizedWindows = null, Func<uint, bool>? canOpenShellSystemSurface = null, Func<bool>? canOpenOnScreenKeyboard = null, Func<bool>? canOpenNarrator = null,
        Func<int, bool>? canLaunchPinnedAppInstance = null, Func<int, bool>? canLaunchPinnedAppInstanceAsAdministrator = null,
        Func<int, bool>? canActivateLastActivePinnedApp = null)
    {
        if (_controlEscapeHeld && key == VK_ESCAPE) return WindowsKeyAction.Suppress;
        if (_replaceControlEscape && key == VK_ESCAPE && controlPressed && !altPressed && !shiftPressed)
        {
            _controlEscapeHeld = true;
            return WindowsKeyAction.Suppress;
        }

        if (key is VK_LWIN or VK_RWIN)
        {
            if (_heldWindowsKey is null)
            {
                _heldWindowsKey = key;
                _forwarded = false;
                return WindowsKeyAction.Suppress;
            }
            if (_heldWindowsKey == key) return WindowsKeyAction.Suppress;
            if (!_forwarded)
            {
                _forwarded = true;
                return WindowsKeyAction.ForwardWindowsDownThenPass;
            }
            return WindowsKeyAction.PassThrough;
        }

        if (_heldWindowsKey is not null && !_forwarded)
        {
            // Let Shift and Control reach the keyboard state while keeping the Windows
            // key pending. This supports Win+Shift+T and Win+Control+number in either order.
            if (key is VK_SHIFT or VK_LSHIFT or VK_RSHIFT or VK_CONTROL or VK_LCONTROL or VK_RCONTROL)
                return WindowsKeyAction.PassThrough;
            if (_suppressedShortcutKeys.Contains(key)) return WindowsKeyAction.Suppress;
            if (shiftPressed && !altPressed &&
                TaskbarShortcutCatalog.GetOneBasedPinIndex(key) is { } newInstancePinIndex)
            {
                var action = controlPressed ? WindowsKeyAction.LaunchPinnedAppInstanceAsAdministrator : WindowsKeyAction.LaunchPinnedAppInstance;
                var canLaunch = controlPressed ? canLaunchPinnedAppInstanceAsAdministrator : canLaunchPinnedAppInstance;
                if (canLaunch?.Invoke(newInstancePinIndex) == true)
                {
                    _taskbarShortcutConsumed = true;
                    TaskbarPinIndex = newInstancePinIndex;
                    _suppressedShortcutKeys.Add(key);
                    return action;
                }
            }
            if (controlPressed && !shiftPressed && !altPressed &&
                TaskbarShortcutCatalog.GetOneBasedPinIndex(key) is { } lastActivePinIndex &&
                canActivateLastActivePinnedApp?.Invoke(lastActivePinIndex) == true)
            {
                _taskbarShortcutConsumed = true;
                TaskbarPinIndex = lastActivePinIndex;
                _suppressedShortcutKeys.Add(key);
                return WindowsKeyAction.ActivateLastActivePinnedApp;
            }
            if (key == (uint)'B' && canFocusTaskbarSystem?.Invoke() == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedShortcutKeys.Add(key);
                return WindowsKeyAction.FocusTaskbarSystem;
            }
            if (key == (uint)'X' && canOpenPowerUserMenu?.Invoke() == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedShortcutKeys.Add(key);
                return WindowsKeyAction.OpenPowerUserMenu;
            }
            if (key == (uint)'R' && canOpenRunDialog?.Invoke() == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedShortcutKeys.Add(key);
                return WindowsKeyAction.OpenRunDialog;
            }
            if (key == (uint)'D' && canToggleDesktop?.Invoke() == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedShortcutKeys.Add(key);
                return WindowsKeyAction.ToggleDesktop;
            }
            if (key == (uint)'M')
            {
                var action = shiftPressed ? WindowsKeyAction.RestoreMinimizedWindows : WindowsKeyAction.MinimizeAllWindows;
                var canRunAction = shiftPressed ? canRestoreMinimizedWindows : canMinimizeAllWindows;
                if (canRunAction?.Invoke() == true)
                {
                    _taskbarShortcutConsumed = true;
                    _suppressedShortcutKeys.Add(key);
                    return action;
                }
            }
            if (key is (uint)'A' or (uint)'F' or (uint)'G' or (uint)'H' or (uint)'I' or (uint)'K' or (uint)'L' or (uint)'N' or (uint)'P' or (uint)'Q' or (uint)'S' or (uint)'U' or (uint)'V' or (uint)'W' or (uint)'Z' or 0x09 or 0x20 && !controlPressed && !altPressed && !shiftPressed &&
                canOpenShellSystemSurface?.Invoke(key) == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedShortcutKeys.Add(key);
                ShellSystemSurfaceKey = key;
                return WindowsKeyAction.OpenShellSystemSurface;
            }
            if (key == (uint)'O' && controlPressed && !altPressed && !shiftPressed && canOpenOnScreenKeyboard?.Invoke() == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedShortcutKeys.Add(key);
                return WindowsKeyAction.OpenOnScreenKeyboard;
            }
            if (key == 0x0D && controlPressed && !altPressed && !shiftPressed && canOpenNarrator?.Invoke() == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedShortcutKeys.Add(key);
                return WindowsKeyAction.OpenNarrator;
            }
            if (key == (uint)'E' && canOpenExplorer?.Invoke() == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedShortcutKeys.Add(key);
                return WindowsKeyAction.OpenExplorer;
            }
            if (key == (uint)'T' && canFocusTaskbar?.Invoke() == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedShortcutKeys.Add(key);
                return shiftPressed ? WindowsKeyAction.FocusTaskbarPrevious : WindowsKeyAction.FocusTaskbar;
            }
            if (TaskbarShortcutCatalog.GetOneBasedPinIndex(key) is { } pinIndex && canActivateTaskbarPin?.Invoke(pinIndex) == true)
            {
                _taskbarShortcutConsumed = true;
                TaskbarPinIndex = pinIndex;
                _suppressedShortcutKeys.Add(key);
                return WindowsKeyAction.ActivateTaskbarPin;
            }
            _forwarded = true;
            return WindowsKeyAction.ForwardWindowsDownThenPass;
        }
        return WindowsKeyAction.PassThrough;
    }

    public WindowsKeyAction KeyUp(uint key)
    {
        if (key == VK_ESCAPE && _controlEscapeHeld)
        {
            _controlEscapeHeld = false;
            return WindowsKeyAction.OpenStartMenu;
        }
        if (_suppressedShortcutKeys.Remove(key)) return WindowsKeyAction.Suppress;
        if (_heldWindowsKey != key) return WindowsKeyAction.PassThrough;
        var action = _forwarded
            ? WindowsKeyAction.ForwardWindowsUpThenSuppress
            : _taskbarShortcutConsumed ? WindowsKeyAction.Suppress
            : _replaceBareWindowsKey ? WindowsKeyAction.OpenStartMenu
            : WindowsKeyAction.ForwardWindowsTapThenSuppress;
        _heldWindowsKey = null;
        _forwarded = false;
        _taskbarShortcutConsumed = false;
        ShellSystemSurfaceKey = null;
        TaskbarPinIndex = null;
        return action;
    }

    public uint? Cancel()
    {
        var forwardedKey = _heldWindowsKey is not null && _forwarded ? _heldWindowsKey : null;
        _heldWindowsKey = null;
        _forwarded = false;
        _taskbarShortcutConsumed = false;
        ShellSystemSurfaceKey = null;
        _suppressedShortcutKeys.Clear();
        _controlEscapeHeld = false;
        TaskbarPinIndex = null;
        return forwardedKey;
    }
}
