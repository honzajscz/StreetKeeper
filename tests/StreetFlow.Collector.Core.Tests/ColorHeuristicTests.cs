using StreetFlow.Collector.Core.Imaging;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class ColorHeuristicTests
{
    [Theory]
    [InlineData(200, 30, 30, "red")]
    [InlineData(240, 130, 20, "orange")]
    [InlineData(235, 220, 40, "yellow")]
    [InlineData(40, 160, 60, "green")]
    [InlineData(40, 80, 200, "blue")]
    [InlineData(150, 60, 200, "purple")]
    [InlineData(15, 15, 15, "black")]
    [InlineData(245, 245, 245, "white")]
    [InlineData(128, 128, 128, "gray")]
    [InlineData(110, 70, 30, "brown")]
    public void Solid_color_image_maps_to_expected_bucket(byte r, byte g, byte b, string expected)
    {
        var image = RgbImage.Solid(r, g, b, 32, 32);
        Assert.Equal(expected, ColorHeuristic.EstimateDominantColor(image));
    }

    [Fact]
    public void Majority_color_wins_in_a_mixed_image()
    {
        // 3/4 blue, 1/4 red.
        var pixels = new byte[16 * 16 * 3];
        for (var i = 0; i < 256; i++)
        {
            var offset = i * 3;
            if (i < 64)
            {
                pixels[offset] = 200; // red
            }
            else
            {
                pixels[offset + 2] = 200; // blue
            }
        }

        Assert.Equal("blue", ColorHeuristic.EstimateDominantColor(new RgbImage(pixels, 16, 16)));
    }

    [Fact]
    public void Rejects_mismatched_buffer()
    {
        Assert.Throws<ArgumentException>(() => new RgbImage(new byte[10], 2, 2));
    }
}
