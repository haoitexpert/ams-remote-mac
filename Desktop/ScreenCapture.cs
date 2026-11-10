// Main-display capture with CoreGraphics. Each call compares the screen with the previous one in 64-pixel tiles and returns
// the bounding box of what changed as one JPEG: the same "one dirty rectangle per frame" the Windows client sends.
using System.Runtime.InteropServices;
using static AMS.RemoteMac.Desktop.CoreGraphics;

namespace AMS.RemoteMac.Desktop;

internal sealed record Frame(int X, int Y, int Width, int Height, byte[] Jpeg);

internal sealed unsafe class ScreenCapture
{
    private const int TileSize = 64;
    private const float JpegQuality = 0.6f;

    private readonly uint _displayId = CGMainDisplayID();
    private byte[]? _previous;
    private int _stride;

    /// <summary>Size of the captured image in pixels (twice the point size on a Retina screen).</summary>
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>The changed part of the screen, or null when nothing changed. The first call returns the whole screen, and so
    /// does the first call after the resolution changes (<see cref="Width"/>/<see cref="Height"/> then differ).</summary>
    public Frame? CaptureChanges()
    {
        var image = CGDisplayCreateImage(_displayId);
        if (image == 0) return null;
        try
        {
            var w = (int)CGImageGetWidth(image);
            var h = (int)CGImageGetHeight(image);
            var stride = (int)CGImageGetBytesPerRow(image);
            var bpp = (int)CGImageGetBitsPerPixel(image) / 8;

            var data = CGDataProviderCopyData(CGImageGetDataProvider(image));
            if (data == 0) return null;
            int x0, y0, x1, y1;
            try
            {
                var length = (int)CFDataGetLength(data);
                var pixels = new ReadOnlySpan<byte>((void*)CFDataGetBytePtr(data), length);

                if (_previous is null || w != Width || h != Height || stride != _stride || _previous.Length != length)
                {
                    Width = w;
                    Height = h;
                    _stride = stride;
                    _previous = new byte[length];
                    (x0, y0, x1, y1) = (0, 0, w, h);
                }
                else if (!DirtyBounds(pixels, _previous, w, h, stride, bpp, out x0, out y0, out x1, out y1))
                {
                    return null;
                }
                pixels.CopyTo(_previous);
            }
            finally
            {
                CFRelease(data);
            }

            var crop = CGImageCreateWithImageInRect(image, new CGRect(x0, y0, x1 - x0, y1 - y0));
            if (crop == 0) return null;
            try
            {
                var jpeg = ToJpeg(crop);
                return jpeg is null ? null : new Frame(x0, y0, x1 - x0, y1 - y0, jpeg);
            }
            finally
            {
                CFRelease(crop);
            }
        }
        finally
        {
            CFRelease(image);
        }
    }

    /// <summary>Forgets the last frame, so the next capture sends the whole screen.</summary>
    public void Reset() => _previous = null;

    private static bool DirtyBounds(ReadOnlySpan<byte> now, byte[] before, int w, int h, int stride, int bpp,
        out int x0, out int y0, out int x1, out int y1)
    {
        x0 = y0 = int.MaxValue;
        x1 = y1 = -1;
        for (var ty = 0; ty < h; ty += TileSize)
        {
            var th = Math.Min(TileSize, h - ty);
            for (var tx = 0; tx < w; tx += TileSize)
            {
                var tw = Math.Min(TileSize, w - tx);
                for (var row = ty; row < ty + th; row++)
                {
                    var offset = row * stride + tx * bpp;
                    if (now.Slice(offset, tw * bpp).SequenceEqual(before.AsSpan(offset, tw * bpp))) continue;
                    x0 = Math.Min(x0, tx);
                    y0 = Math.Min(y0, ty);
                    x1 = Math.Max(x1, tx + tw);
                    y1 = Math.Max(y1, ty + th);
                    break;
                }
            }
        }
        return x1 > 0;
    }

    private static byte[]? ToJpeg(nint image)
    {
        var output = CFDataCreateMutable(0, 0);
        if (output == 0) return null;
        try
        {
            var jpegType = CFStringCreateWithCString(0, "public.jpeg", kCFStringEncodingUTF8);
            var dest = CGImageDestinationCreateWithData(output, jpegType, 1, 0);
            CFRelease(jpegType);
            if (dest == 0) return null;
            try
            {
                var qualityKey = CFStringCreateWithCString(0, "kCGImageDestinationLossyCompressionQuality", kCFStringEncodingUTF8);
                var quality = JpegQuality;
                var qualityValue = CFNumberCreate(0, kCFNumberFloat32Type, ref quality);
                var props = CFDictionaryCreate(0, [qualityKey], [qualityValue], 1, 0, 0);
                CGImageDestinationAddImage(dest, image, props);
                CFRelease(props);
                CFRelease(qualityValue);
                CFRelease(qualityKey);
                if (!CGImageDestinationFinalize(dest)) return null;

                var bytes = new byte[(int)CFDataGetLength(output)];
                Marshal.Copy(CFDataGetBytePtr(output), bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                CFRelease(dest);
            }
        }
        finally
        {
            CFRelease(output);
        }
    }
}
