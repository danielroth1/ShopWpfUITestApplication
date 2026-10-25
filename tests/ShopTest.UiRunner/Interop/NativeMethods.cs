using System.Runtime.InteropServices;

namespace ShopTest.UiRunner.Interop;

/// <summary>
/// Minimal user32 surface: input injection (guest-local only), window capture and DPI awareness.
/// DllImport is used deliberately; the SYSLIB1054 suggestion is suppressed in the project file.
/// </summary>
internal static class NativeMethods
{
    internal const int SmCxScreen = 0;
    internal const int SmCyScreen = 1;

    internal const uint PwRenderFullContent = 0x00000002;

    internal const uint InputMouse = 0;
    internal const uint InputKeyboard = 1;

    internal const uint KeyEventfKeyUp = 0x0002;
    internal const uint KeyEventfUnicode = 0x0004;

    internal const uint MouseEventfMove = 0x0001;
    internal const uint MouseEventfLeftDown = 0x0002;
    internal const uint MouseEventfLeftUp = 0x0004;
    internal const uint MouseEventfWheel = 0x0800;
    internal const uint MouseEventfAbsolute = 0x8000;

    internal const int WheelDelta = 120;

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static void EnablePerMonitorDpiAwareness()
    {
        _ = SetProcessDpiAwarenessContext(PerMonitorAwareV2);
    }

    public static (int Width, int Height) PrimaryScreenSize() =>
        (GetSystemMetrics(SmCxScreen), GetSystemMetrics(SmCyScreen));

    public static bool TryPrintWindow(IntPtr hwnd, IntPtr hdc) =>
        hwnd != IntPtr.Zero && PrintWindow(hwnd, hdc, PwRenderFullContent);

    public static void SendUnicodeText(string text)
    {
        foreach (var character in text)
        {
            var down = Input.KeyboardDown(character);
            var up = Input.KeyboardUp(character);
            _ = SendInput(2, [down, up], Marshal.SizeOf<Input>());
        }
    }

    public static void SendKeyDown(ushort virtualKey, bool keyUp)
    {
        var input = keyUp ? Input.VirtualKeyUp(virtualKey) : Input.VirtualKeyDown(virtualKey);
        _ = SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    public static void MoveTo(int x, int y)
    {
        var (width, height) = PrimaryScreenSize();
        var input = new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = (int)Math.Round(x * 65535.0 / Math.Max(width - 1, 1)),
                    Dy = (int)Math.Round(y * 65535.0 / Math.Max(height - 1, 1)),
                    Flags = MouseEventfMove | MouseEventfAbsolute,
                },
            },
        };

        _ = SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    public static void ClickLeft()
    {
        var down = new Input
        {
            Type = InputMouse,
            Data = new InputUnion { Mouse = new MouseInput { Flags = MouseEventfLeftDown } },
        };
        var up = new Input
        {
            Type = InputMouse,
            Data = new InputUnion { Mouse = new MouseInput { Flags = MouseEventfLeftUp } },
        };

        _ = SendInput(2, [down, up], Marshal.SizeOf<Input>());
    }

    public static void Wheel(int notches) =>
        WheelRaw(unchecked(-WheelDelta * notches));

    public static void WheelRaw(int signedDelta)
    {
        var input = new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput { MouseData = unchecked((uint)signedDelta), Flags = MouseEventfWheel },
            },
        };

        _ = SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Data;

        public static Input KeyboardDown(char character) => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput { Scan = character, Flags = KeyEventfUnicode },
            },
        };

        public static Input KeyboardUp(char character) => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput { Scan = character, Flags = KeyEventfUnicode | KeyEventfKeyUp },
            },
        };

        public static Input VirtualKeyDown(ushort virtualKey) => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey } },
        };

        public static Input VirtualKeyUp(ushort virtualKey) => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = KeyEventfKeyUp } },
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public char Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
