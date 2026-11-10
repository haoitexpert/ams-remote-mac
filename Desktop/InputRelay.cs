// Mouse and keyboard from the browser viewer, posted as CGEvents. Positions arrive as fractions of the screen (0..1), keys
// as KeyboardEvent.key strings ("a", "A", "Enter", "ArrowLeft", "Shift"), buttons as MouseEvent.button (0 left, 1 middle,
// 2 right). macOS needs Accessibility permission for the posted events to reach other apps.
using static AMS.RemoteMac.Desktop.CoreGraphics;

namespace AMS.RemoteMac.Desktop;

internal static class InputRelay
{
    private static readonly object Gate = new();
    private static ulong _modifiers;
    private static int _buttonsDown;
    private static DateTime _lastClickAt;
    private static CGPoint _lastClickPoint;
    private static CGPoint _lastPoint;
    private static int _clickCount;

    public static void MouseMove(double percentX, double percentY)
    {
        lock (Gate)
        {
            var point = _lastPoint = ToScreen(percentX, percentY);
            var (type, button) = (_buttonsDown & 1) != 0 ? (kCGEventLeftMouseDragged, kCGMouseButtonLeft)
                : (_buttonsDown & 4) != 0 ? (kCGEventRightMouseDragged, kCGMouseButtonRight)
                : (_buttonsDown & 2) != 0 ? (kCGEventOtherMouseDragged, kCGMouseButtonCenter)
                : (kCGEventMouseMoved, kCGMouseButtonLeft);
            Post(CGEventCreateMouseEvent(0, type, point, button));
        }
    }

    public static void MouseButton(int browserButton, bool down, double percentX, double percentY)
    {
        lock (Gate)
        {
            var point = _lastPoint = ToScreen(percentX, percentY);
            var (type, button, bit) = browserButton switch
            {
                2 => (down ? kCGEventRightMouseDown : kCGEventRightMouseUp, kCGMouseButtonRight, 4),
                1 => (down ? kCGEventOtherMouseDown : kCGEventOtherMouseUp, kCGMouseButtonCenter, 2),
                _ => (down ? kCGEventLeftMouseDown : kCGEventLeftMouseUp, kCGMouseButtonLeft, 1),
            };

            // macOS reads a double-click from the click count on the event, not from the timing of two clicks.
            if (down)
            {
                var now = DateTime.UtcNow;
                var near = Math.Abs(point.X - _lastClickPoint.X) < 5 && Math.Abs(point.Y - _lastClickPoint.Y) < 5;
                _clickCount = near && (now - _lastClickAt).TotalMilliseconds < 500 ? _clickCount + 1 : 1;
                _lastClickAt = now;
                _lastClickPoint = point;
                _buttonsDown |= bit;
            }
            else
            {
                _buttonsDown &= ~bit;
            }

            var e = CGEventCreateMouseEvent(0, type, point, button);
            CGEventSetIntegerValueField(e, kCGMouseEventClickState, Math.Max(1, _clickCount));
            CGEventSetFlags(e, _modifiers);
            Post(e);
        }
    }

    public static void Wheel(double deltaX, double deltaY)
    {
        // Browser deltas are pixels, positive = content moves up (scroll down); macOS wheel values are the opposite sign.
        var e = CGEventCreateScrollWheelEvent2(0, kCGScrollEventUnitPixel, 2,
            (int)Math.Clamp(-deltaY, -2000, 2000), (int)Math.Clamp(-deltaX, -2000, 2000), 0);
        Post(e);
    }

    public static void KeyDown(string key)
    {
        lock (Gate)
        {
            if (Modifier(key) is { } m)
            {
                if (m.Flag == kCGEventFlagMaskAlphaShift) _modifiers ^= m.Flag;
                else _modifiers |= m.Flag;
                PostKey(m.Code, true);
                return;
            }

            // Plain typing goes in as the character itself, so it is right whatever keyboard layout the Mac uses. With
            // Command, Control or Option held it has to be the physical key, or shortcuts (Cmd+C) do nothing.
            const ulong shortcutMods = kCGEventFlagMaskCommand | kCGEventFlagMaskControl | kCGEventFlagMaskAlternate;
            if (key.Length == 1 && (_modifiers & shortcutMods) == 0)
            {
                TypeText(key);
                return;
            }
            if (KeyCode(key) is { } code) PostKey(code, true);
            else if (key.Length == 1) TypeText(key);
        }
    }

    public static void KeyUp(string key)
    {
        lock (Gate)
        {
            if (Modifier(key) is { } m)
            {
                if (m.Flag != kCGEventFlagMaskAlphaShift) _modifiers &= ~m.Flag;
                PostKey(m.Code, false);
                return;
            }
            if (key.Length == 1 && (_modifiers & (kCGEventFlagMaskCommand | kCGEventFlagMaskControl | kCGEventFlagMaskAlternate)) == 0)
                return;   // typed on key down
            if (KeyCode(key) is { } code) PostKey(code, false);
        }
    }

    public static void KeyPress(string key)
    {
        KeyDown(key);
        KeyUp(key);
    }

    /// <summary>Releases every modifier and mouse button: the viewer lost focus mid-shortcut or mid-drag.</summary>
    public static void ReleaseAll()
    {
        lock (Gate)
        {
            foreach (var (_, code, flag) in Modifiers)
                if (flag != kCGEventFlagMaskAlphaShift && (_modifiers & flag) != 0) PostKey(code, false);
            _modifiers &= kCGEventFlagMaskAlphaShift;

            if ((_buttonsDown & 1) != 0) Post(CGEventCreateMouseEvent(0, kCGEventLeftMouseUp, _lastPoint, kCGMouseButtonLeft));
            if ((_buttonsDown & 4) != 0) Post(CGEventCreateMouseEvent(0, kCGEventRightMouseUp, _lastPoint, kCGMouseButtonRight));
            if ((_buttonsDown & 2) != 0) Post(CGEventCreateMouseEvent(0, kCGEventOtherMouseUp, _lastPoint, kCGMouseButtonCenter));
            _buttonsDown = 0;
        }
    }

    /// <summary>Types text as characters (clipboard "type text", the touch keyboard, non-ASCII keys).</summary>
    public static void TypeText(string text)
    {
        // CGEventKeyboardSetUnicodeString takes at most 20 UTF-16 units per event.
        for (var i = 0; i < text.Length; i += 20)
        {
            var part = text.Substring(i, Math.Min(20, text.Length - i)).ToCharArray();
            foreach (var down in new[] { true, false })
            {
                var e = CGEventCreateKeyboardEvent(0, 0, down);
                CGEventKeyboardSetUnicodeString(e, (nuint)part.Length, part);
                Post(e);
            }
        }
    }

    private static CGPoint ToScreen(double percentX, double percentY)
    {
        // Event positions are in points (the main display's bounds), not in the Retina pixels the stream carries.
        var bounds = CGDisplayBounds(CGMainDisplayID());
        return new CGPoint(
            bounds.Origin.X + Math.Clamp(percentX, 0, 1) * bounds.Size.Width,
            bounds.Origin.Y + Math.Clamp(percentY, 0, 1) * bounds.Size.Height);
    }

    private static void PostKey(ushort code, bool down)
    {
        var e = CGEventCreateKeyboardEvent(0, code, down);
        CGEventSetFlags(e, _modifiers);
        Post(e);
    }

    private static void Post(nint e)
    {
        if (e == 0) return;
        CGEventPost(kCGHIDEventTap, e);
        CFRelease(e);
    }

    private static readonly (string Key, ushort Code, ulong Flag)[] Modifiers =
    {
        ("Shift", 0x38, kCGEventFlagMaskShift),
        ("Control", 0x3B, kCGEventFlagMaskControl),
        ("Alt", 0x3A, kCGEventFlagMaskAlternate),
        ("Meta", 0x37, kCGEventFlagMaskCommand),
        ("OS", 0x37, kCGEventFlagMaskCommand),
        ("CapsLock", 0x39, kCGEventFlagMaskAlphaShift),
    };

    private static (ushort Code, ulong Flag)? Modifier(string key)
    {
        foreach (var (k, code, flag) in Modifiers)
            if (k == key) return (code, flag);
        return null;
    }

    // macOS virtual key codes (kVK_* in Carbon's Events.h), US layout for the character keys.
    private static readonly Dictionary<string, ushort> Named = new()
    {
        ["Enter"] = 0x24, ["Tab"] = 0x30, [" "] = 0x31, ["Backspace"] = 0x33, ["Escape"] = 0x35, ["Delete"] = 0x75,
        ["Insert"] = 0x72, ["Home"] = 0x73, ["End"] = 0x77, ["PageUp"] = 0x74, ["PageDown"] = 0x79,
        ["ArrowLeft"] = 0x7B, ["ArrowRight"] = 0x7C, ["ArrowDown"] = 0x7D, ["ArrowUp"] = 0x7E,
        ["F1"] = 0x7A, ["F2"] = 0x78, ["F3"] = 0x63, ["F4"] = 0x76, ["F5"] = 0x60, ["F6"] = 0x61,
        ["F7"] = 0x62, ["F8"] = 0x64, ["F9"] = 0x65, ["F10"] = 0x6D, ["F11"] = 0x67, ["F12"] = 0x6F,
    };

    private const string CharKeys = "asdfhgzxcv\0bqweryt123465=97-80]ou[ip\0lj'k;\\,/nm.\0\0`";
    private const string ShiftedFrom = "!@#$%^&*()_+{}|:\"<>?~";
    private const string ShiftedTo = "1234567890-=[]\\;',./`";

    private static ushort? KeyCode(string key)
    {
        if (Named.TryGetValue(key, out var named)) return named;
        if (key.Length != 1) return null;
        var c = char.ToLowerInvariant(key[0]);
        var shifted = ShiftedFrom.IndexOf(c);
        if (shifted >= 0) c = ShiftedTo[shifted];
        var index = CharKeys.IndexOf(c);
        return index >= 0 && c != '\0' ? (ushort)index : null;
    }
}
