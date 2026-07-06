using StreetFlow.Collector.Core.Alpr;
using StreetFlow.Collector.Core.Imaging;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class AlprPipelineTests
{
    private sealed class FakeDetector(params PlateDetection[] detections) : IPlateDetector
    {
        public IReadOnlyList<PlateDetection> Detect(RgbImage frame) => detections;
    }

    private sealed class FakeOcr(string text, float confidence = 0.9f) : IPlateOcr
    {
        public RgbImage? LastCrop { get; private set; }

        public OcrResult Recognize(RgbImage plateCrop)
        {
            LastCrop = plateCrop;
            return new OcrResult(text, confidence);
        }
    }

    private static RgbImage BlueFrame() => RgbImage.Solid(30, 60, 200, 128, 96);

    [Fact]
    public void Detection_plus_ocr_yields_a_normalized_candidate_with_color()
    {
        var detector = new FakeDetector(new PlateDetection(new RectI(40, 40, 32, 12), 0.8f));
        var ocr = new FakeOcr("3ao 1234");
        var pipeline = new AlprPipeline(detector, ocr);

        var candidates = pipeline.ProcessFrame(BlueFrame());

        var candidate = Assert.Single(candidates);
        Assert.Equal("3ao 1234", candidate.RawText);
        Assert.Equal("3A01234", candidate.NormalizedPlate);
        Assert.Equal("blue", candidate.Color); // sampled from the (blue) frame around the plate
        Assert.Equal(32, candidate.PlateCrop.Width);
        Assert.Equal(12, candidate.PlateCrop.Height);
        Assert.NotNull(ocr.LastCrop);
        Assert.Equal(32, ocr.LastCrop!.Width);
    }

    [Fact]
    public void No_detections_yield_no_candidates()
    {
        var pipeline = new AlprPipeline(new FakeDetector(), new FakeOcr("3A2 3456"));
        Assert.Empty(pipeline.ProcessFrame(BlueFrame()));
    }

    [Fact]
    public void Low_confidence_detection_is_filtered()
    {
        var detector = new FakeDetector(new PlateDetection(new RectI(40, 40, 32, 12), 0.2f));
        var pipeline = new AlprPipeline(detector, new FakeOcr("3A2 3456"));
        Assert.Empty(pipeline.ProcessFrame(BlueFrame()));
    }

    [Fact]
    public void Low_confidence_ocr_is_filtered()
    {
        var detector = new FakeDetector(new PlateDetection(new RectI(40, 40, 32, 12), 0.8f));
        var pipeline = new AlprPipeline(detector, new FakeOcr("3A2 3456", confidence: 0.1f));
        Assert.Empty(pipeline.ProcessFrame(BlueFrame()));
    }

    [Theory]
    [InlineData("AB")]           // too short after normalization
    [InlineData("AB12CD34EF56")] // too long
    [InlineData("---")]          // normalizes to empty
    public void Implausible_plate_lengths_are_filtered(string text)
    {
        var detector = new FakeDetector(new PlateDetection(new RectI(40, 40, 32, 12), 0.8f));
        var pipeline = new AlprPipeline(detector, new FakeOcr(text));
        Assert.Empty(pipeline.ProcessFrame(BlueFrame()));
    }

    [Fact]
    public void Detection_box_reaching_outside_the_frame_is_clamped_not_fatal()
    {
        var detector = new FakeDetector(new PlateDetection(new RectI(120, 90, 32, 12), 0.9f));
        var pipeline = new AlprPipeline(detector, new FakeOcr("3A2 3456"));

        var candidate = Assert.Single(pipeline.ProcessFrame(BlueFrame()));
        Assert.Equal(8, candidate.PlateCrop.Width);  // 128 - 120
        Assert.Equal(6, candidate.PlateCrop.Height); // 96 - 90
    }

    [Fact]
    public void Multiple_detections_in_one_frame_all_become_candidates()
    {
        var detector = new FakeDetector(
            new PlateDetection(new RectI(10, 10, 32, 12), 0.9f),
            new PlateDetection(new RectI(60, 60, 32, 12), 0.7f));
        var pipeline = new AlprPipeline(detector, new FakeOcr("3A2 3456"));

        Assert.Equal(2, pipeline.ProcessFrame(BlueFrame()).Count);
    }
}
