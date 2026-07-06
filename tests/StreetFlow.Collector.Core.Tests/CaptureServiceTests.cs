using StreetFlow.Collector.Core.Alpr;
using StreetFlow.Collector.Core.Captures;
using StreetFlow.Collector.Core.Imaging;
using StreetFlow.Collector.Core.Plates;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class CaptureServiceTests : IDisposable
{
    private static readonly Guid CampaignId = Guid.Parse("8f7c3f2a-1b2c-4d5e-9f0a-112233445566");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"streetflow-capture-{Guid.NewGuid():N}");
    private readonly FileCaptureStore _store;

    public CaptureServiceTests() => _store = new FileCaptureStore(_root);

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class ScriptedDetector : IPlateDetector
    {
        public PlateDetection[] Next { get; set; } = [];
        public IReadOnlyList<PlateDetection> Detect(RgbImage frame) => Next;
    }

    private sealed class ScriptedOcr : IPlateOcr
    {
        public string NextText { get; set; } = "";
        public OcrResult Recognize(RgbImage plateCrop) => new(NextText, 0.95f);
    }

    private sealed class FakeJpegEncoder : IJpegEncoder
    {
        public byte[] EncodeJpeg(RgbImage image, int quality = 85) => [0xFF, 0xD8, (byte)image.Width];
    }

    private static readonly PlateDetection Detection = new(new RectI(40, 40, 32, 12), 0.9f);

    private (CaptureService Service, ScriptedDetector Detector, ScriptedOcr Ocr) CreateService()
    {
        var detector = new ScriptedDetector();
        var ocr = new ScriptedOcr();
        var service = new CaptureService(
            new AlprPipeline(detector, ocr),
            _store,
            new FakeJpegEncoder(),
            CampaignId,
            "north");
        return (service, detector, ocr);
    }

    private static RgbImage Frame() => RgbImage.Solid(30, 60, 200, 128, 96);

    [Fact]
    public async Task Frame_with_a_plate_produces_a_stored_hashed_capture_with_photo()
    {
        var (service, detector, ocr) = CreateService();
        detector.Next = [Detection];
        ocr.NextText = "3AO 1234";
        var timestamp = DateTimeOffset.UtcNow;

        var stored = await service.ProcessFrameAsync(Frame(), timestamp);

        var record = Assert.Single(stored);
        Assert.Equal(CampaignId.ToString("D"), record.CampaignId);
        Assert.Equal("north", record.PointId);
        Assert.Equal("3AO 1234", record.RawOcrText);
        Assert.Equal("3A01234", record.NormalizedPlate);
        Assert.Equal(
            PlateHasher.ComputeHash("3A01234", PlateHasher.DeriveCampaignSalt(CampaignId)),
            record.PlateHash);
        Assert.Equal(timestamp, record.Timestamp);
        Assert.NotNull(await _store.GetPlateImageAsync(record.LocalId)); // crop photo kept locally for verification
    }

    [Fact]
    public async Task Same_plate_in_consecutive_frames_is_stored_once()
    {
        var (service, detector, ocr) = CreateService();
        detector.Next = [Detection];
        ocr.NextText = "3A2 3456";
        var start = DateTimeOffset.UtcNow;

        Assert.Single(await service.ProcessFrameAsync(Frame(), start));
        Assert.Empty(await service.ProcessFrameAsync(Frame(), start.AddSeconds(1)));
        Assert.Empty(await service.ProcessFrameAsync(Frame(), start.AddSeconds(5)));

        Assert.Single(await _store.GetAllAsync());
    }

    [Fact]
    public async Task Same_plate_after_the_suppression_window_is_a_new_capture()
    {
        var (service, detector, ocr) = CreateService();
        detector.Next = [Detection];
        ocr.NextText = "3A2 3456";
        var start = DateTimeOffset.UtcNow;

        Assert.Single(await service.ProcessFrameAsync(Frame(), start));
        Assert.Single(await service.ProcessFrameAsync(Frame(), start.AddSeconds(15)));

        Assert.Equal(2, (await _store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Different_plates_are_not_suppressed()
    {
        var (service, detector, ocr) = CreateService();
        detector.Next = [Detection];
        var start = DateTimeOffset.UtcNow;

        ocr.NextText = "1AA 1111";
        Assert.Single(await service.ProcessFrameAsync(Frame(), start));
        ocr.NextText = "2BB 2222";
        Assert.Single(await service.ProcessFrameAsync(Frame(), start.AddSeconds(1)));
    }

    [Fact]
    public async Task Empty_frame_stores_nothing()
    {
        var (service, _, _) = CreateService();
        Assert.Empty(await service.ProcessFrameAsync(Frame(), DateTimeOffset.UtcNow));
        Assert.Empty(await _store.GetAllAsync());
    }
}
