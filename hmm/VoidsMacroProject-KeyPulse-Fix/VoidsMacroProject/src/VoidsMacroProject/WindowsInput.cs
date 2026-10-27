using System.Runtime.InteropServices;
using System.Threading;

namespace VoidsMacroProject;

internal static class WindowsInput
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;

    // Send a real down/up pair with a short dwell between the events.
    // Some games miss a down+up pair delivered in the same SendInput batch.
    private const int DefaultPressDurationMs = 20;

    public static void Pulse(string key)
    {
        if (key == "Mouse Left") { PulseMouse(MouseLeftDown, MouseLeftUp); return; }
        if (key == "Mouse Right") { PulseMouse(MouseRightDown, MouseRightUp); return; }
        if (key == "Mouse Middle") { PulseMouse(MouseMiddleDown, MouseMiddleUp); return; }
        if (!KeyCatalog.KeyToVirtualKey.TryGetValue(key, out var vk)) return;

        SendKeyboard(vk, 0);
        Thread.Sleep(DefaultPressDurationMs);
        SendKeyboard(vk, KeyUp);
    }

    private static void SendKeyboard(ushort vk, uint flags)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput { VirtualKey = vk, Flags = flags }
            }
        };
        _ = SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    private static void PulseMouse(uint down, uint up)
    {
        SendMouse(down);
        Thread.Sleep(DefaultPressDurationMs);
        SendMouse(up);
    }

    private static void SendMouse(uint flags)
    {
        var input = new Input
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput { Flags = flags }
            }
        };
        _ = SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInput);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
