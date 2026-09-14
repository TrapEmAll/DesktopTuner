using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopTuner;

public sealed class WindowsKeyStartHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const nuint InjectionMarker = 0x44544b59;

    private readonly Action _showStartMenu;
    private readonly Func<int, bool> _canActivateTaskbarPin;
    private readonly Action<int> _activateTaskbarPin;
    private readonly Func<bool> _canFocusTaskbar;
    private readonly Action<bool> _focusTaskbar;
    private readonly Func<bool> _canOpenExplorer;
    private readonly Action _openExplorer;
    private readonly Func<bool> _canToggleDesktop;
    private readonly Action _toggleDesktop;
    private readonly Func<bool> _canFocusTaskbarSystem;
    private readonly Action _focusTaskbarSystem;
    private readonly Func<bool> _canOpenPowerUserMenu;
    private readonly Action _openPowerUserMenu;
    private readonly Func<bool> _canOpenRunDialog;
    private readonly Action _openRunDialog;
    private readonly HookProc _callback;
    private readonly WindowsKeyGesture _gesture;
    private nint _hook;

    public WindowsKeyStartHook(Action showStartMenu, Func<int, bool>? canActivateTaskbarPin = null, Action<int>? activateTaskbarPin = null, Func<bool>? canFocusTaskbar = null, Action<bool>? focusTaskbar = null, bool replaceBareWindowsKey = true, Func<bool>? canOpenExplorer = null, Action? openExplorer = null, bool replaceControlEscape = false, Func<bool>? canToggleDesktop = null, Action? toggleDesktop = null, Func<bool>? canFocusTaskbarSystem = null, Action? focusTaskbarSystem = null, Func<bool>? canOpenPowerUserMenu = null, Action? openPowerUserMenu = null, Func<bool>? canOpenRunDialog = null, Action? openRunDialog = null)
    {
        _showStartMenu = showStartMenu;
        _canActivateTaskbarPin = replaceBareWindowsKey ? canActivateTaskbarPin ?? (_ => false) : _ => false;
        _activateTaskbarPin = activateTaskbarPin ?? (_ => { });
        _canFocusTaskbar = replaceBareWindowsKey ? canFocusTaskbar ?? (() => false) : () => false;
        _focusTaskbar = focusTaskbar ?? (_ => { });
        _canOpenExplorer = canOpenExplorer ?? (() => false);
        _openExplorer = openExplorer ?? (() => { });
        _canToggleDesktop = canToggleDesktop ?? (() => false);
        _toggleDesktop = toggleDesktop ?? (() => { });
        _canFocusTaskbarSystem = canFocusTaskbarSystem ?? (() => false);
        _focusTaskbarSystem = focusTaskbarSystem ?? (() => { });
        _canOpenPowerUserMenu = canOpenPowerUserMenu ?? (() => false);
        _openPowerUserMenu = openPowerUserMenu ?? (() => { });
        _canOpenRunDialog = canOpenRunDialog ?? (() => false);
        _openRunDialog = openRunDialog ?? (() => { });
        _gesture = new WindowsKeyGesture(replaceBareWindowsKey, replaceControlEscape);
        _callback = KeyboardCallback;
    }

    public bool IsInstalled => _hook != 0;

    public bool TryInstall(out int error)
    {
        if (IsInstalled) { error = 0; return true; }
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        error = _hook == 0 ? Marshal.GetLastWin32Error() : 0;
        return _hook != 0;
    }

    private nint KeyboardCallback(int code, nint wParam, nint lParam)
    {
        if (code != HC_ACTION) return CallNextHookEx(_hook, code, wParam, lParam);
        try
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            if ((data.Flags & LLKHF_INJECTED) != 0 || data.ExtraInfo == InjectionMarker)
                return CallNextHookEx(_hook, code, wParam, lParam);

            var canActivateTaskbarPin = !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canActivateTaskbarPin
                : static _ => false;
            var canFocusTaskbar = !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canFocusTaskbar
                : static () => false;
            var canOpenExplorer = !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canOpenExplorer
                : static () => false;
            var canToggleDesktop = !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canToggleDesktop
                : static () => false;
            var canFocusTaskbarSystem = !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canFocusTaskbarSystem
                : static () => false;
            var canOpenPowerUserMenu = !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canOpenPowerUserMenu
                : static () => false;
            var canOpenRunDialog = !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canOpenRunDialog
                : static () => false;
            var action = message switch
            {
                WM_KEYDOWN or WM_SYSKEYDOWN => _gesture.KeyDown(data.VirtualKey, canActivateTaskbarPin, canFocusTaskbar, canOpenExplorer,
                    IsModifierPressed(VK_CONTROL), IsModifierPressed(VK_MENU), IsModifierPressed(VK_SHIFT), canToggleDesktop, canFocusTaskbarSystem, canOpenPowerUserMenu, canOpenRunDialog),
                WM_KEYUP or WM_SYSKEYUP => _gesture.KeyUp(data.VirtualKey),
                _ => WindowsKeyAction.PassThrough
            };
            switch (action)
            {
                case WindowsKeyAction.Suppress:
                case WindowsKeyAction.ForwardWindowsUpThenSuppress:
                    if (action == WindowsKeyAction.ForwardWindowsUpThenSuppress) SendWindowsKey(data.VirtualKey, keyUp: true);
                    return new nint(1);
                case WindowsKeyAction.ForwardWindowsTapThenSuppress:
                    SendWindowsKey(data.VirtualKey, keyUp: false);
                    SendWindowsKey(data.VirtualKey, keyUp: true);
                    return new nint(1);
                case WindowsKeyAction.ForwardWindowsDownThenPass:
                    SendWindowsKey(_gesture.HeldWindowsKey ?? data.VirtualKey, keyUp: false);
                    break;
                case WindowsKeyAction.OpenStartMenu:
                    Application.Current?.Dispatcher.BeginInvoke(_showStartMenu, DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.ActivateTaskbarPin:
                    if (_gesture.TaskbarPinIndex is { } pinIndex)
                        Application.Current?.Dispatcher.BeginInvoke(() => _activateTaskbarPin(pinIndex), DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.FocusTaskbar:
                    Application.Current?.Dispatcher.BeginInvoke(() => _focusTaskbar(true), DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.FocusTaskbarPrevious:
                    Application.Current?.Dispatcher.BeginInvoke(() => _focusTaskbar(false), DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.OpenExplorer:
                    Application.Current?.Dispatcher.BeginInvoke(_openExplorer, DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.ToggleDesktop:
                    Application.Current?.Dispatcher.BeginInvoke(_toggleDesktop, DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.FocusTaskbarSystem:
                    Application.Current?.Dispatcher.BeginInvoke(_focusTaskbarSystem, DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.OpenPowerUserMenu:
                    Application.Current?.Dispatcher.BeginInvoke(_openPowerUserMenu, DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.OpenRunDialog:
                    Application.Current?.Dispatcher.BeginInvoke(_openRunDialog, DispatcherPriority.Input);
                    return new nint(1);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Windows-key hook callback failed: {ex}");
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static void SendWindowsKey(uint key, bool keyUp)
    {
        var input = new Input
        {
            Type = INPUT_KEYBOARD,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = (ushort)key,
                    Flags = keyUp ? KEYEVENTF_KEYUP : 0,
                    ExtraInfo = InjectionMarker
                }
            }
        };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            Trace.TraceWarning($"Windows-key event injection failed (Windows error {Marshal.GetLastWin32Error()}).");
    }

    private static bool IsModifierPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        var forwardedKey = _gesture.Cancel();
        if (forwardedKey is { } key) SendWindowsKey(key, keyUp: true);
        if (_hook == 0) return;
        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
