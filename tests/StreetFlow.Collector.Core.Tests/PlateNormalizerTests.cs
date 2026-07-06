using StreetFlow.Collector.Core.Plates;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class PlateNormalizerTests
{
    [Fact]
    public void Uppercases_and_strips_spaces()
    {
        Assert.Equal("3A23456", PlateNormalizer.Normalize("3a2 3456"));
    }

    [Theory]
    [InlineData("5B7-2233", "5B72233")]
    [InlineData("5B7.2233", "5B72233")]
    [InlineData(" 5B7 2233 ", "5B72233")]
    [InlineData("5B7\t2233", "5B72233")]
    public void Strips_separators(string raw, string expected)
    {
        Assert.Equal(expected, PlateNormalizer.Normalize(raw));
    }

    [Theory]
    [InlineData("3AO 1234", "3A01234")] // O → 0
    [InlineData("3A0 1234", "3A01234")]
    [InlineData("IA2 3456", "1A23456")] // I → 1
    [InlineData("1A2 3456", "1A23456")]
    [InlineData("3AQ 1234", "3A01234")] // Q → 0
    [InlineData("3AL 1234", "3A11234")] // L → 1
    [InlineData("3AD 1234", "3A01234")] // D → 0
    public void Folds_ocr_confusable_characters_to_one_canonical_form(string raw, string expected)
    {
        Assert.Equal(expected, PlateNormalizer.Normalize(raw));
    }

    [Fact]
    public void Same_car_read_two_ways_yields_identical_normalized_plate()
    {
        // The whole point of normalization (PRD §4): an OCR confusion must not
        // split one car into two different hashes.
        var readingA = PlateNormalizer.Normalize("4AO 3021");
        var readingB = PlateNormalizer.Normalize("4a0-3O21");
        Assert.Equal(readingA, readingB);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    [InlineData("·•·")]
    public void Nothing_plate_like_yields_empty_string(string? raw)
    {
        Assert.Equal(string.Empty, PlateNormalizer.Normalize(raw));
    }

    [Fact]
    public void Drops_non_ascii_noise_but_keeps_plate_characters()
    {
        Assert.Equal("8E48122", PlateNormalizer.Normalize("«8E4 8122»"));
    }
}
