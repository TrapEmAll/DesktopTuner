using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public readonly record struct KeyboardKeyEvent(ushort VirtualKey, bool KeyUp);

public static class SystemFlyoutService
{
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_N = 0x4E;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private static readonly IReadOnlyList<KeyboardKeyEvent> NotificationCenterSequence = Array.AsReadOnly<KeyboardKeyEvent>(
    [
        new(VK_LWIN, false),
        new(VK_N, false),
        new(VK_N, true),
        new(VK_LWIN, true)
    ]);

    public static IReadOnlyList<KeyboardKeyEvent> GetNotificationCenterSequence() => NotificationCenterSequence;

    public static bool OpenNotificationCenter()
    {
        if (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN)) return false;

        Input[] inputs = NotificationCenterSequence.Select(keyEvent => Keyboard(keyEvent.VirtualKey, keyEvent.KeyUp)).ToArray();
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == inputs.Length) return true;

        Input[] releases = [Keyboard(VK_N, keyUp: true), Keyboard(VK_LWIN, keyUp: true)];
        var released = SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
        if (released != releases.Length)
            Trace.TraceError($"Could not release synthetic Windows+N keys after a partial send (released {released} of {releases.Length}).");
        Trace.TraceWarning($"Could not open the Windows notification center (sent {sent} of {inputs.Length} key events; error {Marshal.GetLastWin32Error()}).");
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
