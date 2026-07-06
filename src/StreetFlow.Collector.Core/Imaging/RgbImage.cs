namespace StreetFlow.Collector.Core.Imaging;

/// <summary>Integer rectangle in pixel coordinates.</summary>
public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    /// <summary>Scales the rectangle around its center by <paramref name="factor"/> (used to widen a plate box to a car-body region).</summary>
    public RectI Expand(double factor)
    {
        if (factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Expansion factor must be positive.");
        var newWidth = (int)Math.Round(Width * factor);
        var newHeight = (int)Math.Round(Height * factor);
        return new RectI(X - (newWidth - Width) / 2, Y - (newHeight - Height) / 2, newWidth, newHeight);
    }

    /// <summary>Clamps the rectangle to an image of the given size; may return an empty rectangle.</summary>
    public RectI ClampTo(int imageWidth, int imageHeight)
    {
        var x = Math.Clamp(X, 0, imageWidth);
        var y = Math.Clamp(Y, 0, imageHeight);
        var right = Math.Clamp(Right, 0, imageWidth);
        var bottom = Math.Clamp(Bottom, 0, imageHeight);
        return new RectI(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// A simple packed RGB24 (row-major, 3 bytes per pixel) image. This is the only
/// pixel format the platform-independent core works with; platform layers decode
/// camera frames into it.
/// </summary>
public sealed class RgbImage
{
    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }

    public RgbImage(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Image dimensions must be positive, got {width}x{height}.");
        if (pixels.Length != checked(width * height * 3))
            throw new ArgumentException($"Pixel buffer length {pixels.Length} does not match {width}x{height} RGB24 ({width * height * 3} bytes).", nameof(pixels));

        Pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>Creates a single-color image (test/demo helper).</summary>
    public static RgbImage Solid(byte r, byte g, byte b, int width, int height)
    {
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
        }
        return new RgbImage(pixels, width, height);
    }

    /// <summary>Copies out a sub-rectangle. The rectangle is clamped to the image; an empty result throws.</summary>
    public RgbImage Crop(RectI rect)
    {
        var clamped = rect.ClampTo(Width, Height);
        if (clamped.IsEmpty)
            throw new ArgumentException($"Crop rectangle {rect} does not intersect the {Width}x{Height} image.", nameof(rect));

        var cropped = new byte[clamped.Width * clamped.Height * 3];
        for (var row = 0; row < clamped.Height; row++)
        {
            var sourceOffset = ((clamped.Y + row) * Width + clamped.X) * 3;
            var targetOffset = row * clamped.Width * 3;
            Array.Copy(Pixels, sourceOffset, cropped, targetOffset, clamped.Width * 3);
        }

        return new RgbImage(cropped, clamped.Width, clamped.Height);
    }
}
