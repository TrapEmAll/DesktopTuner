using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public readonly record struct KeyboardKeyEvent(ushort VirtualKey, bool KeyUp);

public static class SystemFlyoutService
{
    public const string NetworkSettingsUri = "ms-settings:network-wifi";
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_A = 0x41;
    private const ushort VK_B = 0x42;
    private const ushort VK_D = 0x44;
    private const ushort VK_OEM_PERIOD = 0xBE;
    private const ushort VK_N = 0x4E;
    private const ushort VK_R = 0x52;
    private const ushort VK_S = 0x53;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_W = 0x57;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private static readonly IReadOnlyList<KeyboardKeyEvent> NotificationCenterSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_N, false),
        new(VK_N, true),
        new(VK_LWIN, true)
    ]);
    private static readonly IReadOnlyList<KeyboardKeyEvent> QuickSettingsSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_A, false),
        new(VK_A, true),
        new(VK_LWIN, true)
    ]);
    private static readonly IReadOnlyList<KeyboardKeyEvent> EmojiPanelSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_OEM_PERIOD, false),
        new(VK_OEM_PERIOD, true),
        new(VK_LWIN, true)
    ]);
    private static readonly IReadOnlyList<KeyboardKeyEvent> NotificationAreaSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_B, false),
        new(VK_B, true),
        new(VK_LWIN, true)
    ]);
    private static readonly IReadOnlyList<KeyboardKeyEvent> WidgetsSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_W, false),
        new(VK_W, true),
        new(VK_LWIN, true)
    ]);
    private static readonly IReadOnlyList<KeyboardKeyEvent> RunDialogSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_R, false),
        new(VK_R, true),
        new(VK_LWIN, true)
    ]);
    private static readonly IReadOnlyList<KeyboardKeyEvent> WindowsSearchSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_S, false),
        new(VK_S, true),
        new(VK_LWIN, true)
    ]);
    private static readonly IReadOnlyList<KeyboardKeyEvent> TaskViewSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_TAB, false),
        new(VK_TAB, true),
        new(VK_LWIN, true)
    ]);
    private static readonly IReadOnlyList<KeyboardKeyEvent> ShowDesktopSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_D, false),
        new(VK_D, true),
        new(VK_LWIN, true)
    ]);

    public static IReadOnlyList<KeyboardKeyEvent> GetNotificationCenterSequence() => NotificationCenterSequence;
    public static IReadOnlyList<KeyboardKeyEvent> GetQuickSettingsSequence() => QuickSettingsSequence;
    public static IReadOnlyList<KeyboardKeyEvent> GetEmojiPanelSequence() => EmojiPanelSequence;
    public static IReadOnlyList<KeyboardKeyEvent> GetNotificationAreaSequence() => NotificationAreaSequence;
    public static IReadOnlyList<KeyboardKeyEvent> GetWidgetsSequence() => WidgetsSequence;
    public static IReadOnlyList<KeyboardKeyEvent> GetRunDialogSequence() => RunDialogSequence;
    public static IReadOnlyList<KeyboardKeyEvent> GetWindowsSearchSequence() => WindowsSearchSequence;
    public static IReadOnlyList<KeyboardKeyEvent> GetTaskViewSequence() => TaskViewSequence;
    public static IReadOnlyList<KeyboardKeyEvent> GetShowDesktopSequence() => ShowDesktopSequence;

    public static bool OpenNotificationCenter() => SendWindowsShortcut(VK_N, NotificationCenterSequence, "notification center");

    public static bool OpenQuickSettings() => SendWindowsShortcut(VK_A, QuickSettingsSequence, "Quick Settings");

    public static bool OpenEmojiPanel() => SendWindowsShortcut(VK_OEM_PERIOD, EmojiPanelSequence, "emoji panel");

    public static bool FocusNotificationArea() => SendWindowsShortcut(VK_B, NotificationAreaSequence, "notification area");

    public static bool OpenWidgets() => SendWindowsShortcut(VK_W, WidgetsSequence, "Widgets board");

    public static bool OpenRunDialog() => SendWindowsShortcut(VK_R, RunDialogSequence, "Run dialog");

    public static bool OpenWindowsSearch() => SendWindowsShortcut(VK_S, WindowsSearchSequence, "Windows Search");

    public static bool OpenTaskView() => SendWindowsShortcut(VK_TAB, TaskViewSequence, "Task View");

    public static bool ShowDesktop() => SendWindowsShortcut(VK_D, ShowDesktopSequence, "Show desktop");

    private static bool SendWindowsShortcut(ushort shortcutKey, IReadOnlyList<KeyboardKeyEvent> sequence, string featureName)
    {
        if (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN)) return false;

        Input[] inputs = sequence.Select(keyEvent => Keyboard(keyEvent.VirtualKey, keyEvent.KeyUp)).ToArray();
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == inputs.Length) return true;

        Input[] releases = [Keyboard(shortcutKey, keyUp: true), Keyboard(VK_LWIN, keyUp: true)];
        var released = SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
        if (released != releases.Length)
            Trace.TraceError($"Could not release synthetic Windows+{(char)shortcutKey} keys after a partial send (released {released} of {releases.Length}).");
        Trace.TraceWarning($"Could not access the Windows {featureName} (sent {sent} of {inputs.Length} key events; error {Marshal.GetLastWin32Error()}).");
        return false;
    }

    private static bool IsKeyDown(ushort key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static Input Keyboard(ushort key, bool keyUp) => new()
    {
        Type = INPUT_KEYBOARD,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = key,
                Flags = keyUp ? KEYEVENTF_KEYUP : 0
            }
        }
    };

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
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
