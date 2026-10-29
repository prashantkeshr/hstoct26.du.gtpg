using System.Runtime.InteropServices;

namespace DhurtaHost.Services;

/// <summary>
/// Injects mouse and keyboard input via Win32 SendInput.
/// Coordinates received from the browser are pixel-based relative to the
/// captured virtual screen and are converted to the 0-65535 absolute range.
/// </summary>
public sealed class InputService
{
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const int WHEEL_DELTA = 120;

    private readonly ScreenCaptureService _capture;
    private readonly ILogger<InputService> _log;

    public InputService(ScreenCaptureService capture, ILogger<InputService> log)
    {
        _capture = capture;
        _log = log;
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────

    public void MouseMove(double x, double y)
    {
        int sw = _capture.ScreenWidth > 0 ? _capture.ScreenWidth : 1920;
        int sh = _capture.ScreenHeight > 0 ? _capture.ScreenHeight : 1080;

        int absX = (int)(x / sw * 65535.0);
        int absY = (int)(y / sh * 65535.0);
        absX = Math.Clamp(absX, 0, 65535);
        absY = Math.Clamp(absY, 0, 65535);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = absX,
                dy = absY,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            }
        };
        Send(input);
    }

    public void MouseButton(string button, bool down)
    {
        uint flags = button switch
        {
            "right" => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            "middle" => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
        };

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT { dwFlags = flags }
        };
        Send(input);
    }

    public void MouseScroll(double deltaX, double deltaY)
    {
        // Vertical scroll: negative deltaY = scroll up, positive = scroll down
        // Win32: positive mouseData = scroll forward (up), negative = backward (down)
        if (deltaY != 0)
        {
            int amount = deltaY > 0 ? -WHEEL_DELTA : WHEEL_DELTA;
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)amount),
                    dwFlags = MOUSEEVENTF_WHEEL,
                }
            };
            Send(input);
        }

        if (deltaX != 0)
        {
            int amount = deltaX > 0 ? WHEEL_DELTA : -WHEEL_DELTA;
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)amount),
                    dwFlags = MOUSEEVENTF_HWHEEL,
                }
            };
            Send(input);
        }
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    public void KeyEvent(string? key, string? code, bool down)
    {
        ushort vk = ResolveVk(code, key);
        if (vk == 0)
        {
            _log.LogDebug("No VK mapping for key={Key} code={Code}", key, code);
            return;
        }

        bool extended = IsExtendedKey(code, vk);
        uint flags = (down ? KEYEVENTF_KEYDOWN : KEYEVENTF_KEYUP)
                     | (extended ? KEYEVENTF_EXTENDEDKEY : 0u);

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = vk, dwFlags = flags }
        };
        Send(input);
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    private static void Send(INPUT input)
    {
        INPUT[] inputs = [input];
        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    // ── VK lookup ─────────────────────────────────────────────────────────────

    private static ushort ResolveVk(string? code, string? key)
    {
        if (code is not null && CodeVk.TryGetValue(code, out var vkCode)) return vkCode;
        if (key is not null && KeyVk.TryGetValue(key, out var vkKey)) return vkKey;
        return 0;
    }

    private static bool IsExtendedKey(string? code, ushort vk)
    {
        // Right-side modifiers and navigation cluster need EXTENDEDKEY flag
        if (code is "ControlRight" or "AltRight" or "MetaRight") return true;
        return vk is >= 0x21 and <= 0x28  // navigation: PgUp/PgDn/End/Home/Arrows
            or 0x2D or 0x2E               // Insert, Delete
            or 0x5B or 0x5C              // Win keys
            or 0x6F                       // Numpad Divide
            or 0x0D;                      // could be NumpadEnter — covered by code check
    }

    // ── Key tables ────────────────────────────────────────────────────────────

    // Browser e.code → Win32 VK (physical key position)
    private static readonly Dictionary<string, ushort> CodeVk = new()
    {
        // Letters
        { "KeyA", 0x41 }, { "KeyB", 0x42 }, { "KeyC", 0x43 }, { "KeyD", 0x44 },
        { "KeyE", 0x45 }, { "KeyF", 0x46 }, { "KeyG", 0x47 }, { "KeyH", 0x48 },
        { "KeyI", 0x49 }, { "KeyJ", 0x4A }, { "KeyK", 0x4B }, { "KeyL", 0x4C },
        { "KeyM", 0x4D }, { "KeyN", 0x4E }, { "KeyO", 0x4F }, { "KeyP", 0x50 },
        { "KeyQ", 0x51 }, { "KeyR", 0x52 }, { "KeyS", 0x53 }, { "KeyT", 0x54 },
        { "KeyU", 0x55 }, { "KeyV", 0x56 }, { "KeyW", 0x57 }, { "KeyX", 0x58 },
        { "KeyY", 0x59 }, { "KeyZ", 0x5A },

        // Digits
        { "Digit0", 0x30 }, { "Digit1", 0x31 }, { "Digit2", 0x32 }, { "Digit3", 0x33 },
        { "Digit4", 0x34 }, { "Digit5", 0x35 }, { "Digit6", 0x36 }, { "Digit7", 0x37 },
        { "Digit8", 0x38 }, { "Digit9", 0x39 },

        // Function keys
        { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 },
        { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 },
        { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B },

        // Control
        { "Enter", 0x0D }, { "NumpadEnter", 0x0D },
        { "Backspace", 0x08 }, { "Tab", 0x09 }, { "Escape", 0x1B }, { "Space", 0x20 },
        { "Delete", 0x2E }, { "Insert", 0x2D },
        { "Home", 0x24 }, { "End", 0x23 },
        { "PageUp", 0x21 }, { "PageDown", 0x22 },
        { "ArrowLeft", 0x25 }, { "ArrowUp", 0x26 }, { "ArrowRight", 0x27 }, { "ArrowDown", 0x28 },
        { "CapsLock", 0x14 }, { "NumLock", 0x90 }, { "ScrollLock", 0x91 },
        { "PrintScreen", 0x2C }, { "Pause", 0x13 },

        // Modifiers
        { "ShiftLeft", 0xA0 }, { "ShiftRight", 0xA1 },
        { "ControlLeft", 0xA2 }, { "ControlRight", 0xA3 },
        { "AltLeft", 0xA4 }, { "AltRight", 0xA5 },
        { "MetaLeft", 0x5B }, { "MetaRight", 0x5C },

        // Punctuation (US layout)
        { "Minus", 0xBD }, { "Equal", 0xBB },
        { "BracketLeft", 0xDB }, { "BracketRight", 0xDD },
        { "Backslash", 0xDC }, { "Semicolon", 0xBA },
        { "Quote", 0xDE }, { "Backquote", 0xC0 },
        { "Comma", 0xBC }, { "Period", 0xBE }, { "Slash", 0xBF },

        // Numpad
        { "Numpad0", 0x60 }, { "Numpad1", 0x61 }, { "Numpad2", 0x62 }, { "Numpad3", 0x63 },
        { "Numpad4", 0x64 }, { "Numpad5", 0x65 }, { "Numpad6", 0x66 }, { "Numpad7", 0x67 },
        { "Numpad8", 0x68 }, { "Numpad9", 0x69 },
        { "NumpadMultiply", 0x6A }, { "NumpadAdd", 0x6B }, { "NumpadSubtract", 0x6D },
        { "NumpadDecimal", 0x6E }, { "NumpadDivide", 0x6F },
    };

    // Browser e.key → Win32 VK (fallback for keys not in CodeVk)
    private static readonly Dictionary<string, ushort> KeyVk = new()
    {
        { "Enter", 0x0D }, { "Backspace", 0x08 }, { "Tab", 0x09 },
        { "Escape", 0x1B }, { " ", 0x20 },
        { "ArrowLeft", 0x25 }, { "ArrowUp", 0x26 }, { "ArrowRight", 0x27 }, { "ArrowDown", 0x28 },
        { "Home", 0x24 }, { "End", 0x23 }, { "PageUp", 0x21 }, { "PageDown", 0x22 },
        { "Insert", 0x2D }, { "Delete", 0x2E },
        { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 },
        { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 },
        { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B },
        { "Shift", 0x10 }, { "Control", 0x11 }, { "Alt", 0x12 },
        { "Meta", 0x5B }, { "CapsLock", 0x14 },
        { "PrintScreen", 0x2C }, { "ScrollLock", 0x91 }, { "Pause", 0x13 },
    };
}

// ── P/Invoke types ────────────────────────────────────────────────────────────

internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}

// On x64 Windows, INPUT is 40 bytes: type(4) + padding(4) + union(32)
[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct INPUT
{
    [FieldOffset(0)] public int type;
    [FieldOffset(8)] public MOUSEINPUT mi;
    [FieldOffset(8)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}
