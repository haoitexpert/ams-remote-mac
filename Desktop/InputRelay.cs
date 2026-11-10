// macOS input injection — reference: MeshAgent mac_events.c
// Translates Windows VK codes (sent by the browser viewer) to macOS keycodes and posts them via CGEvent.
using static AMS.RemoteMac.Desktop.CoreGraphics;

namespace AMS.RemoteMac.Desktop;

internal static class InputRelay
{
    private static bool _lMouseDown;
    private static bool _rMouseDown;
    private static bool _capsLock;

    // Windows VK → macOS keycode mapping (from MeshAgent mac_events.c g_keymap, 114 entries).
    // macOS keycodes from <Carbon/Events.h> (kVK_*).
    private static readonly (ushort macKey, byte winVk)[] KeyMap =
    {
        (0x31, 0x20), // kVK_Space → VK_SPACE
        (0x39, 0x14), // kVK_CapsLock → VK_CAPITAL
        (0x0C, 0x51), // kVK_ANSI_Q → VK_Q
        (0x0D, 0x57), // kVK_ANSI_W → VK_W
        (0x0E, 0x45), // kVK_ANSI_E → VK_E
        (0x0F, 0x52), // kVK_ANSI_R → VK_R
        (0x11, 0x54), // kVK_ANSI_T → VK_T
        (0x10, 0x59), // kVK_ANSI_Y → VK_Y
        (0x20, 0x55), // kVK_ANSI_U → VK_U
        (0x22, 0x49), // kVK_ANSI_I → VK_I
        (0x1F, 0x4F), // kVK_ANSI_O → VK_O
        (0x23, 0x50), // kVK_ANSI_P → VK_P
        (0x00, 0x41), // kVK_ANSI_A → VK_A
        (0x01, 0x53), // kVK_ANSI_S → VK_S
        (0x02, 0x44), // kVK_ANSI_D → VK_D
        (0x03, 0x46), // kVK_ANSI_F → VK_F
        (0x05, 0x47), // kVK_ANSI_G → VK_G
        (0x04, 0x48), // kVK_ANSI_H → VK_H
        (0x26, 0x4A), // kVK_ANSI_J → VK_J
        (0x28, 0x4B), // kVK_ANSI_K → VK_K
        (0x25, 0x4C), // kVK_ANSI_L → VK_L
        (0x06, 0x5A), // kVK_ANSI_Z → VK_Z
        (0x07, 0x58), // kVK_ANSI_X → VK_X
        (0x08, 0x43), // kVK_ANSI_C → VK_C
        (0x09, 0x56), // kVK_ANSI_V → VK_V
        (0x0B, 0x42), // kVK_ANSI_B → VK_B
        (0x2D, 0x4E), // kVK_ANSI_N → VK_N
        (0x2E, 0x4D), // kVK_ANSI_M → VK_M
        (0x12, 0x31), // kVK_ANSI_1 → VK_1
        (0x13, 0x32), // kVK_ANSI_2 → VK_2
        (0x14, 0x33), // kVK_ANSI_3 → VK_3
        (0x15, 0x34), // kVK_ANSI_4 → VK_4
        (0x17, 0x35), // kVK_ANSI_5 → VK_5
        (0x16, 0x36), // kVK_ANSI_6 → VK_6
        (0x1A, 0x37), // kVK_ANSI_7 → VK_7
        (0x1C, 0x38), // kVK_ANSI_8 → VK_8
        (0x19, 0x39), // kVK_ANSI_9 → VK_9
        (0x1D, 0x30), // kVK_ANSI_0 → VK_0
        (0x33, 0x08), // kVK_Delete → VK_BACK
        (0x30, 0x09), // kVK_Tab → VK_TAB
        (0x47, 0x0C), // kVK_ANSI_KeypadClear → VK_CLEAR
        (0x24, 0x0D), // kVK_Return → VK_RETURN
        (0x35, 0x1B), // kVK_Escape → VK_ESCAPE
        (0x75, 0x2E), // kVK_ForwardDelete → VK_DELETE
        (0x73, 0x24), // kVK_Home → VK_HOME
        (0x7B, 0x25), // kVK_LeftArrow → VK_LEFT
        (0x7E, 0x26), // kVK_UpArrow → VK_UP
        (0x7C, 0x27), // kVK_RightArrow → VK_RIGHT
        (0x7D, 0x28), // kVK_DownArrow → VK_DOWN
        (0x74, 0x21), // kVK_PageUp → VK_PRIOR
        (0x79, 0x22), // kVK_PageDown → VK_NEXT
        (0x77, 0x23), // kVK_End → VK_END
        (0x7A, 0x70), // kVK_F1 → VK_F1
        (0x78, 0x71), // kVK_F2 → VK_F2
        (0x63, 0x72), // kVK_F3 → VK_F3
        (0x76, 0x73), // kVK_F4 → VK_F4
        (0x60, 0x74), // kVK_F5 → VK_F5
        (0x61, 0x75), // kVK_F6 → VK_F6
        (0x62, 0x76), // kVK_F7 → VK_F7
        (0x64, 0x77), // kVK_F8 → VK_F8
        (0x65, 0x78), // kVK_F9 → VK_F9
        (0x6D, 0x79), // kVK_F10 → VK_F10
        (0x67, 0x7A), // kVK_F11 → VK_F11
        (0x6F, 0x7B), // kVK_F12 → VK_F12
        (0x69, 0x7C), // kVK_F13 → VK_F13
        (0x6B, 0x7D), // kVK_F14 → VK_F14
        (0x71, 0x7E), // kVK_F15 → VK_F15
        (0x6A, 0x7F), // kVK_F16 → VK_F16
        (0x40, 0x80), // kVK_F17 → VK_F17
        (0x4F, 0x81), // kVK_F18 → VK_F18
        (0x50, 0x82), // kVK_F19 → VK_F19
        (0x5A, 0x83), // kVK_F20 → VK_F20
        (0x43, 0x6A), // kVK_ANSI_KeypadMultiply → VK_MULTIPLY
        (0x18, 0x6B), // kVK_ANSI_Equal → VK_ADD
        (0x2B, 0x6C), // kVK_ANSI_Comma → VK_SEPARATOR
        (0x1B, 0x6D), // kVK_ANSI_Minus → VK_SUBTRACT
        (0x41, 0x6E), // kVK_ANSI_KeypadDecimal → VK_DECIMAL
        (0x4B, 0x6F), // kVK_ANSI_KeypadDivide → VK_DIVIDE
        (0x52, 0x60), // kVK_ANSI_Keypad0 → VK_NUMPAD0
        (0x53, 0x61), // kVK_ANSI_Keypad1 → VK_NUMPAD1
        (0x54, 0x62), // kVK_ANSI_Keypad2 → VK_NUMPAD2
        (0x55, 0x63), // kVK_ANSI_Keypad3 → VK_NUMPAD3
        (0x56, 0x64), // kVK_ANSI_Keypad4 → VK_NUMPAD4
        (0x57, 0x65), // kVK_ANSI_Keypad5 → VK_NUMPAD5
        (0x58, 0x66), // kVK_ANSI_Keypad6 → VK_NUMPAD6
        (0x59, 0x67), // kVK_ANSI_Keypad7 → VK_NUMPAD7
        (0x5B, 0x68), // kVK_ANSI_Keypad8 → VK_NUMPAD8
        (0x5C, 0x69), // kVK_ANSI_Keypad9 → VK_NUMPAD9
        (0x38, 0x10), // kVK_Shift → VK_SHIFT
        (0x3B, 0x11), // kVK_Control → VK_CONTROL
        (0x3A, 0x12), // kVK_Option → VK_MENU (Alt)
        (0x37, 0x5C), // kVK_Command → VK_RWIN
        (0x37, 0x5B), // kVK_Command → VK_LWIN
        (0x29, 0xBA), // kVK_ANSI_Semicolon → VK_OEM_1
        (0x18, 0xBB), // kVK_ANSI_Equal → VK_OEM_PLUS
        (0x2B, 0xBC), // kVK_ANSI_Comma → VK_OEM_COMMA
        (0x1B, 0xBD), // kVK_ANSI_Minus → VK_OEM_MINUS
        (0x2F, 0xBE), // kVK_ANSI_Period → VK_OEM_PERIOD
        (0x2C, 0xBF), // kVK_ANSI_Slash → VK_OEM_2
        (0x32, 0xC0), // kVK_ANSI_Grave → VK_OEM_3
        (0x21, 0xDB), // kVK_ANSI_LeftBracket → VK_OEM_4
        (0x2A, 0xDC), // kVK_ANSI_Backslash → VK_OEM_5
        (0x1E, 0xDD), // kVK_ANSI_RightBracket → VK_OEM_6
        (0x27, 0xDE), // kVK_ANSI_Quote → VK_OEM_7
    };

    /// <summary>
    /// Moves the mouse and/or clicks/scrolls. Parameters match the MeshAgent MouseAction signature
    /// (absX, absY in logical screen coordinates; button from MOUSEEVENTF_*; wheel delta).
    /// </summary>
    public static void MouseAction(double absX, double absY, int button, short wheel)
    {
        var point = new CGPoint(absX, absY);
        var source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);

        if (_lMouseDown || _rMouseDown)
        {
            var dragType = _lMouseDown ? kCGEventLeftMouseDragged : kCGEventRightMouseDragged;
            var e = CGEventCreateMouseEvent(source, dragType, point, 1);
            CGEventPost(kCGHIDEventTap, e);
            CGEventPost(kCGSessionEventTap, e);
            CFRelease(e);
        }
        else
        {
            var e = CGEventCreateMouseEvent(source, kCGEventMouseMoved, point, 0);
            CGEventPost(kCGHIDEventTap, e);
            CGEventPost(kCGSessionEventTap, e);
            CFRelease(e);
        }

        if (button != 0)
        {
            const int MOUSEEVENTF_LEFTDOWN = 0x02;
            const int MOUSEEVENTF_LEFTUP = 0x04;
            const int MOUSEEVENTF_RIGHTDOWN = 0x08;
            const int MOUSEEVENTF_RIGHTUP = 0x10;
            const int MOUSEEVENTF_DOUBLECLICK = 0x88;

            int eventType;
            switch (button)
            {
                case MOUSEEVENTF_LEFTDOWN:
                    _lMouseDown = true;
                    eventType = kCGEventLeftMouseDown;
                    break;
                case MOUSEEVENTF_LEFTUP:
                    _lMouseDown = false;
                    eventType = kCGEventLeftMouseUp;
                    break;
                case MOUSEEVENTF_RIGHTDOWN:
                    _rMouseDown = true;
                    eventType = kCGEventRightMouseDown;
                    break;
                case MOUSEEVENTF_RIGHTUP:
                    _rMouseDown = false;
                    eventType = kCGEventRightMouseUp;
                    break;
                case MOUSEEVENTF_DOUBLECLICK:
                    var dbl = CGEventCreateMouseEvent(source, kCGEventLeftMouseDown, point, 1);
                    CGEventSetIntegerValueField(dbl, kCGMouseEventClickState, 2);
                    CGEventPost(kCGHIDEventTap, dbl);
                    CGEventSetType(dbl, kCGEventLeftMouseUp);
                    CGEventPost(kCGHIDEventTap, dbl);
                    CFRelease(dbl);
                    if (source != 0) CFRelease(source);
                    return;
                default:
                    if (source != 0) CFRelease(source);
                    return;
            }

            var ev = CGEventCreateMouseEvent(source, eventType, point, 1);
            CGEventPost(kCGHIDEventTap, ev);
            CFRelease(ev);
        }
        else if (wheel != 0)
        {
            var ev = CGEventCreateScrollWheelEvent(source, kCGScrollEventUnitPixel, 1, wheel);
            CGEventPost(kCGHIDEventTap, ev);
            CFRelease(ev);
        }

        if (source != 0) CFRelease(source);
    }

    /// <summary>
    /// Injects a keyboard event. vk is a Windows VK code; keyUp true = release.
    /// </summary>
    public static void KeyAction(byte vk, bool keyUp)
    {
        if (vk == 0x14 && keyUp) // VK_CAPITAL
        {
            _capsLock = !_capsLock;
            return;
        }

        ushort macKeycode = 0;
        var found = false;
        foreach (var (mac, win) in KeyMap)
        {
            if (win == vk)
            {
                macKeycode = mac;
                found = true;
                break;
            }
        }
        if (!found) return;

        var source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);
        var key = CGEventCreateKeyboardEvent(source, macKeycode, !keyUp);
        if (_capsLock) CGEventSetFlags(key, kCGEventFlagMaskAlphaShift);
        CGEventPost(kCGHIDEventTap, key);
        CFRelease(key);
        if (source != 0) CFRelease(source);
    }

    /// <summary>Injects a Unicode character (for text input that doesn't map to a VK code).</summary>
    public static void KeyActionUnicode(ushort unicode, bool keyUp)
    {
        if (keyUp) return;
        var key = CGEventCreateKeyboardEvent(0, 0, true);
        CGEventKeyboardSetUnicodeString(key, 1, ref unicode);
        CGEventPost(kCGHIDEventTap, key);
        CFRelease(key);
    }
}
