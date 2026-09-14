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
    RestoreMinimizedWindows
}

public sealed class WindowsKeyGesture
{
    private const uint VK_LWIN = 0x5b;
    private const uint VK_RWIN = 0x5c;
    private const uint VK_SHIFT = 0x10;
    private const uint VK_LSHIFT = 0xa0;
    private const uint VK_RSHIFT = 0xa1;
    private const uint VK_ESCAPE = 0x1b;
    private uint? _heldWindowsKey;
    private bool _forwarded;
    private bool _taskbarShortcutConsumed;
    private readonly bool _replaceBareWindowsKey;
    private readonly bool _replaceControlEscape;
    private bool _controlEscapeHeld;
    private readonly HashSet<uint> _suppressedShortcutKeys = [];
    public int? TaskbarPinIndex { get; private set; }

    public WindowsKeyGesture(bool replaceBareWindowsKey = true, bool replaceControlEscape = false)
    {
        _replaceBareWindowsKey = replaceBareWindowsKey;
        _replaceControlEscape = replaceControlEscape;
    }

    public uint? HeldWindowsKey => _heldWindowsKey;

    public WindowsKeyAction KeyDown(uint key, Func<int, bool>? canActivateTaskbarPin = null, Func<bool>? canFocusTaskbar = null, Func<bool>? canOpenExplorer = null,
        bool controlPressed = false, bool altPressed = false, bool shiftPressed = false, Func<bool>? canToggleDesktop = null,
        Func<bool>? canFocusTaskbarSystem = null, Func<bool>? canOpenPowerUserMenu = null, Func<bool>? canOpenRunDialog = null,
        Func<bool>? canMinimizeAllWindows = null, Func<bool>? canRestoreMinimizedWindows = null)
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
            // Let Shift reach the keyboard state while keeping the Windows key pending.
            // That lets Win+Shift+T use the custom reverse taskbar cycle regardless of
            // whether Shift or the Windows key was pressed first.
            if (key is VK_SHIFT or VK_LSHIFT or VK_RSHIFT) return WindowsKeyAction.PassThrough;
            if (_suppressedShortcutKeys.Contains(key)) return WindowsKeyAction.Suppress;
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
        TaskbarPinIndex = null;
        return action;
    }

    public uint? Cancel()
    {
        var forwardedKey = _heldWindowsKey is not null && _forwarded ? _heldWindowsKey : null;
        _heldWindowsKey = null;
        _forwarded = false;
        _taskbarShortcutConsumed = false;
        _suppressedShortcutKeys.Clear();
        _controlEscapeHeld = false;
        TaskbarPinIndex = null;
        return forwardedKey;
    }
}
