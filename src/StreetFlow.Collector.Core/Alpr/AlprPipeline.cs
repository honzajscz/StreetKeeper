using StreetFlow.Collector.Core.Imaging;
using StreetFlow.Collector.Core.Plates;

namespace StreetFlow.Collector.Core.Alpr;

public sealed class AlprPipelineOptions
{
    /// <summary>Detections below this confidence are discarded.</summary>
    public float MinDetectionConfidence { get; init; } = 0.45f;

    /// <summary>OCR readings below this confidence are discarded.</summary>
    public float MinOcrConfidence { get; init; } = 0.40f;

    /// <summary>Normalized plates shorter than this are treated as misreads (Czech plates are 5–8 chars).</summary>
    public int MinPlateLength { get; init; } = 5;

    /// <summary>Normalized plates longer than this are treated as misreads.</summary>
    public int MaxPlateLength { get; init; } = 8;

    /// <summary>How much the plate box is widened to sample car-body pixels for the color heuristic.</summary>
    public double ColorRegionExpandFactor { get; init; } = 2.5;
}

/// <summary>
/// One usable plate reading extracted from a frame: the plate crop (stays on the
/// phone), raw OCR text, its normalized form and the estimated vehicle color.
/// Hashing happens later in <see cref="Captures.CaptureService"/> because it
/// needs the campaign salt.
/// </summary>
public sealed record PlateCaptureCandidate(
    RgbImage PlateCrop,
    string RawText,
    string NormalizedPlate,
    string Color,
    float DetectionConfidence,
    float OcrConfidence);

/// <summary>
/// Orchestrates the per-frame on-device chain of spec §5:
/// detect plate → crop → OCR → normalize → color heuristic.
/// Pure orchestration over <see cref="IPlateDetector"/>/<see cref="IPlateOcr"/>,
/// testable with fakes; the ONNX-backed implementations plug in from the ML layer.
/// </summary>
public sealed class AlprPipeline
{
    private readonly IPlateDetector _detector;
    private readonly IPlateOcr _ocr;
    private readonly AlprPipelineOptions _options;

    public AlprPipeline(IPlateDetector detector, IPlateOcr ocr, AlprPipelineOptions? options = null)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
        _options = options ?? new AlprPipelineOptions();
    }

    /// <summary>Processes one camera frame and returns all usable plate readings (possibly none).</summary>
    public IReadOnlyList<PlateCaptureCandidate> ProcessFrame(RgbImage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var candidates = new List<PlateCaptureCandidate>();
        foreach (var detection in _detector.Detect(frame))
        {
            if (detection.Confidence < _options.MinDetectionConfidence)
                continue;

            var plateBox = detection.Box.ClampTo(frame.Width, frame.Height);
            if (plateBox.IsEmpty)
                continue;

            var plateCrop = frame.Crop(plateBox);
            var ocrResult = _ocr.Recognize(plateCrop);
            if (ocrResult.Confidence < _options.MinOcrConfidence)
                continue;

            var normalized = PlateNormalizer.Normalize(ocrResult.Text);
            if (normalized.Length < _options.MinPlateLength || normalized.Length > _options.MaxPlateLength)
                continue;

            // Color comes from a widened region around the plate so it samples the
            // car body, not just the (white) plate itself.
            var colorRegion = plateBox.Expand(_options.ColorRegionExpandFactor).ClampTo(frame.Width, frame.Height);
            var color = ColorHeuristic.EstimateDominantColor(frame.Crop(colorRegion));

            candidates.Add(new PlateCaptureCandidate(
                plateCrop,
                ocrResult.Text,
                normalized,
                color,
                detection.Confidence,
                ocrResult.Confidence));
        }

        return candidates;
    }
}
