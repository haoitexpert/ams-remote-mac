// P/Invoke bindings for macOS CoreGraphics — screen capture and input injection.
// Reference: MeshAgent meshcore/KVM/MacOS/mac_kvm.c + mac_events.c
using System.Runtime.InteropServices;

namespace AMS.RemoteMac.Desktop;

internal static class CoreGraphics
{
    private const string CgLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CfLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // ── Display ──

    [DllImport(CgLib)] public static extern uint CGMainDisplayID();
    [DllImport(CgLib)] public static extern nint CGDisplayCreateImage(uint displayId);
    [DllImport(CgLib)] public static extern nint CGDisplayModeGetPixelWidth(nint mode);
    [DllImport(CgLib)] public static extern nint CGDisplayModeGetPixelHeight(nint mode);
    [DllImport(CgLib)] public static extern nint CGDisplayCopyDisplayMode(uint displayId);
    [DllImport(CgLib)] public static extern nuint CGDisplayPixelsWide(uint displayId);
    [DllImport(CgLib)] public static extern nuint CGDisplayPixelsHigh(uint displayId);

    // ── Image ──

    [DllImport(CgLib)] public static extern nuint CGImageGetWidth(nint image);
    [DllImport(CgLib)] public static extern nuint CGImageGetHeight(nint image);
    [DllImport(CgLib)] public static extern nint CGImageGetDataProvider(nint image);
    [DllImport(CgLib)] public static extern nint CGDataProviderCopyData(nint provider);

    // ── CFData ──

    [DllImport(CfLib)] public static extern nint CFDataGetLength(nint cfData);
    [DllImport(CfLib)] public static extern nint CFDataGetBytePtr(nint cfData);
    [DllImport(CfLib)] public static extern void CFRelease(nint cf);

    // ── Bitmap context (for JPEG encoding) ──

    [DllImport(CgLib)] public static extern nint CGColorSpaceCreateDeviceRGB();
    [DllImport(CgLib)] public static extern nint CGBitmapContextCreate(nint data, nuint width, nuint height,
        nuint bitsPerComponent, nuint bytesPerRow, nint colorSpace, uint bitmapInfo);
    [DllImport(CgLib)] public static extern void CGContextDrawImage(nint context, CGRect rect, nint image);
    [DllImport(CgLib)] public static extern nint CGBitmapContextCreateImage(nint context);
    [DllImport(CgLib)] public static extern void CGContextRelease(nint context);
    [DllImport(CgLib)] public static extern void CGColorSpaceRelease(nint colorSpace);

    // ── Image destination (JPEG) ──

    private const string ImgLib = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    [DllImport(ImgLib)] public static extern nint CGImageDestinationCreateWithData(nint data, nint type, nuint count, nint options);
    [DllImport(ImgLib)] public static extern void CGImageDestinationAddImage(nint dest, nint image, nint properties);
    [DllImport(ImgLib)] public static extern bool CGImageDestinationFinalize(nint dest);

    [DllImport(CfLib)] public static extern nint CFDataCreateMutable(nint allocator, nint capacity);
    [DllImport(CfLib)] public static extern nint CFStringCreateWithCString(nint allocator, string cStr, uint encoding);
    [DllImport(CfLib)] public static extern nint CFDictionaryCreate(nint allocator, nint[] keys, nint[] values,
        nint numValues, nint keyCallBacks, nint valueCallBacks);
    [DllImport(CfLib)] public static extern nint CFNumberCreate(nint allocator, nint theType, ref float value);

    public const uint kCFStringEncodingUTF8 = 0x08000100;
    public const nint kCFNumberFloat32Type = 5;

    // ── Mouse events ──

    [DllImport(CgLib)] public static extern nint CGEventSourceCreate(int stateId);
    [DllImport(CgLib)] public static extern nint CGEventCreateMouseEvent(nint source, int mouseType, CGPoint point, int mouseButton);
    [DllImport(CgLib)] public static extern nint CGEventCreateScrollWheelEvent(nint source, int units, uint wheelCount, int wheel1);
    [DllImport(CgLib)] public static extern void CGEventPost(int tap, nint eventRef);
    [DllImport(CgLib)] public static extern void CGEventSetIntegerValueField(nint eventRef, int field, long value);
    [DllImport(CgLib)] public static extern void CGEventSetType(nint eventRef, int type);

    // ── Keyboard events ──

    [DllImport(CgLib)] public static extern nint CGEventCreateKeyboardEvent(nint source, ushort virtualKey, bool keyDown);
    [DllImport(CgLib)] public static extern void CGEventSetFlags(nint eventRef, ulong flags);
    [DllImport(CgLib)] public static extern void CGEventKeyboardSetUnicodeString(nint eventRef, nuint stringLength, ref ushort unicodeString);

    // ── Permission check ──

    [DllImport(CgLib)] public static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(CgLib)] public static extern bool CGRequestScreenCaptureAccess();

    // ── Constants ──

    public const int kCGEventSourceStateHIDSystemState = 1;
    public const int kCGHIDEventTap = 0;
    public const int kCGSessionEventTap = 1;

    // Mouse event types
    public const int kCGEventLeftMouseDown = 1;
    public const int kCGEventLeftMouseUp = 2;
    public const int kCGEventRightMouseDown = 3;
    public const int kCGEventRightMouseUp = 4;
    public const int kCGEventMouseMoved = 5;
    public const int kCGEventLeftMouseDragged = 6;
    public const int kCGEventRightMouseDragged = 7;

    // Scroll
    public const int kCGScrollEventUnitPixel = 1;

    // Click state field
    public const int kCGMouseEventClickState = 1;

    // Key modifier flags
    public const ulong kCGEventFlagMaskAlphaShift = 0x00010000;
    public const ulong kCGEventFlagMaskShift = 0x00020000;
    public const ulong kCGEventFlagMaskControl = 0x00040000;
    public const ulong kCGEventFlagMaskAlternate = 0x00080000;
    public const ulong kCGEventFlagMaskCommand = 0x00100000;

    // Bitmap info
    public const uint kCGBitmapByteOrder32Little = 2 << 12;
    public const uint kCGImageAlphaPremultipliedFirst = 2;

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
