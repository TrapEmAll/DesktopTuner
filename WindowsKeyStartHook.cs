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
    private readonly Func<bool> _canMinimizeAllWindows;
    private readonly Action _minimizeAllWindows;
    private readonly Func<bool> _canRestoreMinimizedWindows;
    private readonly Action _restoreMinimizedWindows;
    private readonly Func<uint, bool> _canOpenShellSystemSurface;
    private readonly Action<uint> _openShellSystemSurface;
    private readonly Func<bool> _canOpenOnScreenKeyboard;
    private readonly Action _openOnScreenKeyboard;
    private readonly Func<int, bool> _canLaunchPinnedAppInstance;
    private readonly Action<int> _launchPinnedAppInstance;
    private readonly Func<int, bool> _canLaunchPinnedAppInstanceAsAdministrator;
    private readonly Action<int> _launchPinnedAppInstanceAsAdministrator;
    private readonly Func<int, bool> _canActivateLastActivePinnedApp;
    private readonly Action<int> _activateLastActivePinnedApp;
    private readonly HookProc _callback;
    private readonly WindowsKeyGesture _gesture;
    private uint? _pendingShellSystemSurfaceKey;
    private bool _shellSystemSurfaceShortcutKeyReleased;
    private bool _shellSystemSurfaceWindowsKeyReleased;
    private nint _hook;

    public WindowsKeyStartHook(Action showStartMenu, Func<int, bool>? canActivateTaskbarPin = null, Action<int>? activateTaskbarPin = null, Func<bool>? canFocusTaskbar = null, Action<bool>? focusTaskbar = null, bool replaceBareWindowsKey = true, Func<bool>? canOpenExplorer = null, Action? openExplorer = null, bool replaceControlEscape = false, Func<bool>? canToggleDesktop = null, Action? toggleDesktop = null, Func<bool>? canFocusTaskbarSystem = null, Action? focusTaskbarSystem = null, Func<bool>? canOpenPowerUserMenu = null, Action? openPowerUserMenu = null, Func<bool>? canOpenRunDialog = null, Action? openRunDialog = null, Func<bool>? canMinimizeAllWindows = null, Action? minimizeAllWindows = null, Func<bool>? canRestoreMinimizedWindows = null, Action? restoreMinimizedWindows = null, Func<uint, bool>? canOpenShellSystemSurface = null, Action<uint>? openShellSystemSurface = null, Func<bool>? canOpenOnScreenKeyboard = null, Action? openOnScreenKeyboard = null, Func<int, bool>? canLaunchPinnedAppInstance = null, Action<int>? launchPinnedAppInstance = null, Func<int, bool>? canLaunchPinnedAppInstanceAsAdministrator = null, Action<int>? launchPinnedAppInstanceAsAdministrator = null, Func<int, bool>? canActivateLastActivePinnedApp = null, Action<int>? activateLastActivePinnedApp = null)
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
        _canMinimizeAllWindows = canMinimizeAllWindows ?? (() => false);
        _minimizeAllWindows = minimizeAllWindows ?? (() => { });
        _canRestoreMinimizedWindows = canRestoreMinimizedWindows ?? (() => false);
        _restoreMinimizedWindows = restoreMinimizedWindows ?? (() => { });
        _canOpenShellSystemSurface = canOpenShellSystemSurface ?? (_ => false);
        _openShellSystemSurface = openShellSystemSurface ?? (_ => { });
        _canOpenOnScreenKeyboard = canOpenOnScreenKeyboard ?? (() => false);
        _openOnScreenKeyboard = openOnScreenKeyboard ?? (() => { });
        _canLaunchPinnedAppInstance = canLaunchPinnedAppInstance ?? (_ => false);
        _launchPinnedAppInstance = launchPinnedAppInstance ?? (_ => { });
        _canLaunchPinnedAppInstanceAsAdministrator = canLaunchPinnedAppInstanceAsAdministrator ?? (_ => false);
        _launchPinnedAppInstanceAsAdministrator = launchPinnedAppInstanceAsAdministrator ?? (_ => { });
        _canActivateLastActivePinnedApp = canActivateLastActivePinnedApp ?? (_ => false);
        _activateLastActivePinnedApp = activateLastActivePinnedApp ?? (_ => { });
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
            var canMinimizeAllWindows = !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canMinimizeAllWindows
                : static () => false;
            var canRestoreMinimizedWindows = IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canRestoreMinimizedWindows
                : static () => false;
            var canOpenShellSystemSurface = !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canOpenShellSystemSurface
                : static _ => false;
            var canOpenOnScreenKeyboard = IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_MENU)
                ? _canOpenOnScreenKeyboard
                : static () => false;
            var canLaunchPinnedAppInstance = IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canLaunchPinnedAppInstance
                : static _ => false;
            var canLaunchPinnedAppInstanceAsAdministrator = IsModifierPressed(VK_SHIFT) && IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_MENU)
                ? _canLaunchPinnedAppInstanceAsAdministrator
                : static _ => false;
            var canActivateLastActivePinnedApp = IsModifierPressed(VK_CONTROL) && !IsModifierPressed(VK_SHIFT) && !IsModifierPressed(VK_MENU)
                ? _canActivateLastActivePinnedApp
                : static _ => false;
            var action = message switch
            {
                WM_KEYDOWN or WM_SYSKEYDOWN => _gesture.KeyDown(data.VirtualKey, canActivateTaskbarPin, canFocusTaskbar, canOpenExplorer,
                    IsModifierPressed(VK_CONTROL), IsModifierPressed(VK_MENU), IsModifierPressed(VK_SHIFT), canToggleDesktop, canFocusTaskbarSystem, canOpenPowerUserMenu, canOpenRunDialog, canMinimizeAllWindows, canRestoreMinimizedWindows, canOpenShellSystemSurface, canOpenOnScreenKeyboard, canLaunchPinnedAppInstance, canLaunchPinnedAppInstanceAsAdministrator, canActivateLastActivePinnedApp),
                WM_KEYUP or WM_SYSKEYUP => _gesture.KeyUp(data.VirtualKey),
                _ => WindowsKeyAction.PassThrough
            };
            if (action == WindowsKeyAction.OpenShellSystemSurface)
            {
                _pendingShellSystemSurfaceKey = _gesture.ShellSystemSurfaceKey;
                _shellSystemSurfaceShortcutKeyReleased = false;
                _shellSystemSurfaceWindowsKeyReleased = false;
            }
            if ((message is WM_KEYUP or WM_SYSKEYUP) && _pendingShellSystemSurfaceKey is { } pendingKey)
            {
                if (data.VirtualKey == pendingKey) _shellSystemSurfaceShortcutKeyReleased = true;
                if (data.VirtualKey is 0x5b or 0x5c) _shellSystemSurfaceWindowsKeyReleased = true;
                if (_shellSystemSurfaceShortcutKeyReleased && _shellSystemSurfaceWindowsKeyReleased)
                {
                    _pendingShellSystemSurfaceKey = null;
                    _shellSystemSurfaceShortcutKeyReleased = false;
                    _shellSystemSurfaceWindowsKeyReleased = false;
                    Application.Current?.Dispatcher.BeginInvoke(() => _openShellSystemSurface(pendingKey), DispatcherPriority.Background);
                }
            }
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
                case WindowsKeyAction.MinimizeAllWindows:
                    Application.Current?.Dispatcher.BeginInvoke(_minimizeAllWindows, DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.RestoreMinimizedWindows:
                    Application.Current?.Dispatcher.BeginInvoke(_restoreMinimizedWindows, DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.OpenShellSystemSurface:
                    return new nint(1);
                case WindowsKeyAction.OpenOnScreenKeyboard:
                    Application.Current?.Dispatcher.BeginInvoke(_openOnScreenKeyboard, DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.LaunchPinnedAppInstance:
                    if (_gesture.TaskbarPinIndex is { } newInstancePinIndex)
                        Application.Current?.Dispatcher.BeginInvoke(() => _launchPinnedAppInstance(newInstancePinIndex), DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.LaunchPinnedAppInstanceAsAdministrator:
                    if (_gesture.TaskbarPinIndex is { } elevatedNewInstancePinIndex)
                        Application.Current?.Dispatcher.BeginInvoke(() => _launchPinnedAppInstanceAsAdministrator(elevatedNewInstancePinIndex), DispatcherPriority.Input);
                    return new nint(1);
                case WindowsKeyAction.ActivateLastActivePinnedApp:
                    if (_gesture.TaskbarPinIndex is { } lastActivePinIndex)
                        Application.Current?.Dispatcher.BeginInvoke(() => _activateLastActivePinnedApp(lastActivePinIndex), DispatcherPriority.Input);
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
        _pendingShellSystemSurfaceKey = null;
        _shellSystemSurfaceShortcutKeyReleased = false;
        _shellSystemSurfaceWindowsKeyReleased = false;
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
