using SkiaSharp;
using StreetFlow.Collector.Core.Imaging;

namespace StreetFlow.Collector.Alpr;

/// <summary>
/// SkiaSharp-backed image encoding/decoding between platform image formats and
/// the core's <see cref="RgbImage"/>. Used by the MAUI app for camera frames and
/// by the eval tool for dataset files.
/// </summary>
public sealed class SkiaImageCodec : IJpegEncoder
{
    public byte[] EncodeJpeg(RgbImage image, int quality = 85)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var bitmap = ToBitmap(image);
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    /// <summary>Decodes an encoded image (JPEG/PNG/…) into packed RGB24.</summary>
    public static RgbImage Decode(byte[] encodedImage)
    {
        using var bitmap = SKBitmap.Decode(encodedImage)
            ?? throw new InvalidDataException("Could not decode image data.");
        return ToRgbImage(bitmap);
    }

    /// <summary>Decodes an image file into packed RGB24.</summary>
    public static RgbImage DecodeFile(string path)
    {
        using var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"Could not decode image file '{path}'.");
        return ToRgbImage(bitmap);
    }

    public static RgbImage ToRgbImage(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var pixels = new byte[bitmap.Width * bitmap.Height * 3];
        var index = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                pixels[index++] = color.Red;
                pixels[index++] = color.Green;
                pixels[index++] = color.Blue;
            }
        }
        return new RgbImage(pixels, bitmap.Width, bitmap.Height);
    }

    private static SKBitmap ToBitmap(RgbImage image)
    {
        var bitmap = new SKBitmap(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var index = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(image.Pixels[index], image.Pixels[index + 1], image.Pixels[index + 2]));
                index += 3;
            }
        }
        return bitmap;
    }
}
