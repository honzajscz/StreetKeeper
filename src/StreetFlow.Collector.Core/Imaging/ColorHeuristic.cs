namespace StreetFlow.Collector.Core.Imaging;

/// <summary>
/// Estimates the dominant vehicle color from pixels of a crop around the plate —
/// a plain heuristic, deliberately no ML model (spec §3.7). The color acts only as
/// a safeguard for hash matching on the dashboard, so a coarse bucket is enough.
/// Buckets are emitted as lowercase English tokens (stable wire values).
/// </summary>
public static class ColorHeuristic
{
    private const int SampleTargetPixels = 4_096; // subsample large crops for speed

    /// <summary>Estimates the dominant color bucket of the image.</summary>
    public static string EstimateDominantColor(RgbImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var counts = new Dictionary<string, int>();
        var totalPixels = image.Width * image.Height;
        var stride = Math.Max(1, totalPixels / SampleTargetPixels);

        for (var pixel = 0; pixel < totalPixels; pixel += stride)
        {
            var offset = pixel * 3;
            var bucket = ClassifyPixel(image.Pixels[offset], image.Pixels[offset + 1], image.Pixels[offset + 2]);
            counts[bucket] = counts.GetValueOrDefault(bucket) + 1;
        }

        return counts.MaxBy(kv => kv.Value).Key;
    }

    /// <summary>Classifies one RGB pixel into a coarse color bucket via HSV rules.</summary>
    public static string ClassifyPixel(byte r, byte g, byte b)
    {
        var (hue, saturation, value) = RgbToHsv(r, g, b);

        // Achromatic buckets first: very dark, very light, or washed-out pixels.
        if (value < 0.16) return "black";
        if (saturation < 0.18 && value > 0.82) return "white";
        if (saturation < 0.22) return "gray";

        // Dark warm hues read as brown rather than orange/red.
        if (hue is >= 10 and < 50 && value < 0.55) return "brown";

        return hue switch
        {
            < 15 or >= 335 => "red",
            < 45 => "orange",
            < 70 => "yellow",
            < 170 => "green",
            < 260 => "blue",
            < 335 => "purple",
            _ => "red",
        };
    }

    private static (double Hue, double Saturation, double Value) RgbToHsv(byte r, byte g, byte b)
    {
        var rf = r / 255.0;
        var gf = g / 255.0;
        var bf = b / 255.0;

        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;

        double hue = 0;
        if (delta > 0)
        {
            if (max == rf)
                hue = 60 * (((gf - bf) / delta) % 6);
            else if (max == gf)
                hue = 60 * ((bf - rf) / delta + 2);
            else
                hue = 60 * ((rf - gf) / delta + 4);
            if (hue < 0)
                hue += 360;
        }

        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }
}
