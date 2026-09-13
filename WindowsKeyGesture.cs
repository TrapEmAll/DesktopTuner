namespace DesktopTuner;

public enum WindowsKeyAction
{
    PassThrough,
    Suppress,
    ForwardWindowsDownThenPass,
    ForwardWindowsUpThenSuppress,
    OpenStartMenu,
    ActivateTaskbarPin,
    FocusTaskbar
}

public sealed class WindowsKeyGesture
{
    private const uint VK_LWIN = 0x5b;
    private const uint VK_RWIN = 0x5c;
    private uint? _heldWindowsKey;
    private bool _forwarded;
    private bool _taskbarShortcutConsumed;
    private readonly HashSet<uint> _suppressedTaskbarKeys = [];
    public int? TaskbarPinIndex { get; private set; }

    public uint? HeldWindowsKey => _heldWindowsKey;

    public WindowsKeyAction KeyDown(uint key, Func<int, bool>? canActivateTaskbarPin = null, Func<bool>? canFocusTaskbar = null)
    {
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
            if (_suppressedTaskbarKeys.Contains(key)) return WindowsKeyAction.Suppress;
            if (key == (uint)'T' && canFocusTaskbar?.Invoke() == true)
            {
                _taskbarShortcutConsumed = true;
                _suppressedTaskbarKeys.Add(key);
                return WindowsKeyAction.FocusTaskbar;
            }
            if (TaskbarShortcutCatalog.GetOneBasedPinIndex(key) is { } pinIndex && canActivateTaskbarPin?.Invoke(pinIndex) == true)
            {
                _taskbarShortcutConsumed = true;
                TaskbarPinIndex = pinIndex;
                _suppressedTaskbarKeys.Add(key);
                return WindowsKeyAction.ActivateTaskbarPin;
            }
            _forwarded = true;
            return WindowsKeyAction.ForwardWindowsDownThenPass;
        }
        return WindowsKeyAction.PassThrough;
    }

    public WindowsKeyAction KeyUp(uint key)
    {
        if (_suppressedTaskbarKeys.Remove(key)) return WindowsKeyAction.Suppress;
        if (_heldWindowsKey != key) return WindowsKeyAction.PassThrough;
        var action = _forwarded
            ? WindowsKeyAction.ForwardWindowsUpThenSuppress
            : _taskbarShortcutConsumed ? WindowsKeyAction.Suppress : WindowsKeyAction.OpenStartMenu;
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
        _suppressedTaskbarKeys.Clear();
        TaskbarPinIndex = null;
        return forwardedKey;
    }
}
