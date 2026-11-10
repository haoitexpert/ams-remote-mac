// P/Invoke bindings for macOS CoreGraphics, CoreFoundation and ImageIO — screen capture and input injection.
// Variadic C functions (CGEventCreateScrollWheelEvent) cannot be called through P/Invoke on Apple silicon, where variadic
// arguments go on the stack: use their fixed-argument variants.
using System.Runtime.InteropServices;

namespace AMS.RemoteMac.Desktop;

internal static class CoreGraphics
{
    private const string CgLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CfLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ImgLib = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    // ── Display ──

    [DllImport(CgLib)] public static extern uint CGMainDisplayID();
    [DllImport(CgLib)] public static extern nint CGDisplayCreateImage(uint displayId);
    [DllImport(CgLib)] public static extern CGRect CGDisplayBounds(uint displayId);

    // ── Image ──

    [DllImport(CgLib)] public static extern nuint CGImageGetWidth(nint image);
    [DllImport(CgLib)] public static extern nuint CGImageGetHeight(nint image);
    [DllImport(CgLib)] public static extern nuint CGImageGetBytesPerRow(nint image);
    [DllImport(CgLib)] public static extern nuint CGImageGetBitsPerPixel(nint image);
    [DllImport(CgLib)] public static extern nint CGImageGetDataProvider(nint image);
    [DllImport(CgLib)] public static extern nint CGImageCreateWithImageInRect(nint image, CGRect rect);
    [DllImport(CgLib)] public static extern nint CGDataProviderCopyData(nint provider);

    // ── CoreFoundation ──

    [DllImport(CfLib)] public static extern nint CFDataGetLength(nint cfData);
    [DllImport(CfLib)] public static extern nint CFDataGetBytePtr(nint cfData);
    [DllImport(CfLib)] public static extern void CFRelease(nint cf);
    [DllImport(CfLib)] public static extern nint CFDataCreateMutable(nint allocator, nint capacity);
    [DllImport(CfLib)] public static extern nint CFStringCreateWithCString(nint allocator, string cStr, uint encoding);
    [DllImport(CfLib)] public static extern nint CFDictionaryCreate(nint allocator, nint[] keys, nint[] values,
        nint numValues, nint keyCallBacks, nint valueCallBacks);
    [DllImport(CfLib)] public static extern nint CFNumberCreate(nint allocator, nint theType, ref float value);

    public const uint kCFStringEncodingUTF8 = 0x08000100;
    public const nint kCFNumberFloat32Type = 5;

    // ── Image destination (JPEG) ──

    [DllImport(ImgLib)] public static extern nint CGImageDestinationCreateWithData(nint data, nint type, nuint count, nint options);
    [DllImport(ImgLib)] public static extern void CGImageDestinationAddImage(nint dest, nint image, nint properties);
    [DllImport(ImgLib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool CGImageDestinationFinalize(nint dest);

    // ── Events ──

    [DllImport(CgLib)] public static extern nint CGEventSourceCreate(int stateId);
    [DllImport(CgLib)] public static extern nint CGEventCreateMouseEvent(nint source, int mouseType, CGPoint point, int mouseButton);
    [DllImport(CgLib)] public static extern nint CGEventCreateScrollWheelEvent2(nint source, int units, uint wheelCount, int wheel1, int wheel2, int wheel3);
    [DllImport(CgLib)] public static extern nint CGEventCreateKeyboardEvent(nint source, ushort virtualKey, [MarshalAs(UnmanagedType.U1)] bool keyDown);
    [DllImport(CgLib)] public static extern void CGEventKeyboardSetUnicodeString(nint eventRef, nuint stringLength, char[] unicodeString);
    [DllImport(CgLib)] public static extern void CGEventSetIntegerValueField(nint eventRef, int field, long value);
    [DllImport(CgLib)] public static extern void CGEventSetFlags(nint eventRef, ulong flags);
    [DllImport(CgLib)] public static extern void CGEventPost(int tap, nint eventRef);

    // ── Permission ──

    private const string AppServicesLib = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(CgLib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(CgLib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool CGRequestScreenCaptureAccess();
    [DllImport(AppServicesLib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool AXIsProcessTrusted();
    [DllImport(AppServicesLib)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool AXIsProcessTrustedWithOptions(nint options);

    /// <summary>Shows macOS's "would like to control this computer" prompt, which also lists this app under Accessibility.</summary>
    public static void PromptForAccessibility()
    {
        // kAXTrustedCheckOptionPrompt is CFSTR("AXTrustedCheckOptionPrompt"); kCFBooleanTrue is a global, read from the library.
        var cf = NativeLibrary.Load(CfLib);
        var cfTrue = Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFBooleanTrue"));
        var key = CFStringCreateWithCString(0, "AXTrustedCheckOptionPrompt", kCFStringEncodingUTF8);
        var options = CFDictionaryCreate(0, [key], [cfTrue], 1, 0, 0);
        AXIsProcessTrustedWithOptions(options);
        CFRelease(options);
        CFRelease(key);
    }

    // ── Constants ──

    public const int kCGEventSourceStateHIDSystemState = 1;
    public const int kCGHIDEventTap = 0;

    public const int kCGEventLeftMouseDown = 1;
    public const int kCGEventLeftMouseUp = 2;
    public const int kCGEventRightMouseDown = 3;
    public const int kCGEventRightMouseUp = 4;
    public const int kCGEventMouseMoved = 5;
    public const int kCGEventLeftMouseDragged = 6;
    public const int kCGEventRightMouseDragged = 7;
    public const int kCGEventOtherMouseDown = 25;
    public const int kCGEventOtherMouseUp = 26;
    public const int kCGEventOtherMouseDragged = 27;

    public const int kCGMouseButtonLeft = 0;
    public const int kCGMouseButtonRight = 1;
    public const int kCGMouseButtonCenter = 2;

    public const int kCGScrollEventUnitPixel = 0;
    public const int kCGMouseEventClickState = 1;

    public const ulong kCGEventFlagMaskAlphaShift = 0x00010000;
    public const ulong kCGEventFlagMaskShift = 0x00020000;
    public const ulong kCGEventFlagMaskControl = 0x00040000;
    public const ulong kCGEventFlagMaskAlternate = 0x00080000;
    public const ulong kCGEventFlagMaskCommand = 0x00100000;

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
        public CGPoint(double x, double y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
        public CGRect(double x, double y, double w, double h) { Origin = new(x, y); Size = new() { Width = w, Height = h }; }
    }
}
