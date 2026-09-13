namespace DesktopTuner;

public enum WindowsKeyAction
{
    PassThrough,
    Suppress,
    ForwardWindowsDownThenPass,
    ForwardWindowsUpThenSuppress,
    OpenStartMenu
}

public sealed class WindowsKeyGesture
{
    private const uint VK_LWIN = 0x5b;
    private const uint VK_RWIN = 0x5c;
    private uint? _heldWindowsKey;
    private bool _forwarded;

    public uint? HeldWindowsKey => _heldWindowsKey;

    public WindowsKeyAction KeyDown(uint key)
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
            _forwarded = true;
            return WindowsKeyAction.ForwardWindowsDownThenPass;
        }
        return WindowsKeyAction.PassThrough;
    }

    public WindowsKeyAction KeyUp(uint key)
    {
        if (_heldWindowsKey != key) return WindowsKeyAction.PassThrough;
        var action = _forwarded ? WindowsKeyAction.ForwardWindowsUpThenSuppress : WindowsKeyAction.OpenStartMenu;
        _heldWindowsKey = null;
        _forwarded = false;
        return action;
    }

    public uint? Cancel()
    {
        var forwardedKey = _heldWindowsKey is not null && _forwarded ? _heldWindowsKey : null;
        _heldWindowsKey = null;
        _forwarded = false;
        return forwardedKey;
    }
}
