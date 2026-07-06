using StreetFlow.Collector.Core.Alpr;
using StreetFlow.Collector.Core.Imaging;
using StreetFlow.Collector.Core.Plates;

namespace StreetFlow.Collector.Core.Captures;

public sealed class CaptureServiceOptions
{
    /// <summary>
    /// A plate seen again at the same point within this window is treated as the
    /// same passage (consecutive camera frames of one car), not a new capture.
    /// </summary>
    public TimeSpan DuplicateSuppressionWindow { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>JPEG quality for the locally stored plate crop.</summary>
    public int PlateImageJpegQuality { get; init; } = 85;
}

/// <summary>
/// Per-session capture orchestrator for one campaign point: runs the ALPR
/// pipeline on each sampled frame, hashes the normalized plate with the campaign
/// salt, suppresses duplicate consecutive readings and writes the capture (with
/// its plate crop photo) to the local store. Nothing here sends data anywhere —
/// sync is a separate, explicit step (spec §3.10).
/// </summary>
public sealed class CaptureService
{
    private readonly AlprPipeline _pipeline;
    private readonly ICaptureStore _store;
    private readonly IJpegEncoder _jpegEncoder;
    private readonly CaptureServiceOptions _options;
    private readonly string _campaignId;
    private readonly string _pointId;
    private readonly string _campaignSalt;
    private readonly Dictionary<string, DateTimeOffset> _lastSeen = [];

    public CaptureService(
        AlprPipeline pipeline,
        ICaptureStore store,
        IJpegEncoder jpegEncoder,
        Guid campaignId,
        string pointId,
        CaptureServiceOptions? options = null)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _jpegEncoder = jpegEncoder ?? throw new ArgumentNullException(nameof(jpegEncoder));
        ArgumentException.ThrowIfNullOrWhiteSpace(pointId);
        _campaignId = campaignId.ToString("D");
        _pointId = pointId;
        _campaignSalt = PlateHasher.DeriveCampaignSalt(campaignId);
        _options = options ?? new CaptureServiceOptions();
    }

    /// <summary>
    /// Processes one sampled camera frame; returns the records stored for it
    /// (empty when nothing usable was detected or everything was a duplicate).
    /// </summary>
    public async Task<IReadOnlyList<CaptureRecord>> ProcessFrameAsync(
        RgbImage frame,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        var stored = new List<CaptureRecord>();

        foreach (var candidate in _pipeline.ProcessFrame(frame))
        {
            if (IsDuplicate(candidate.NormalizedPlate, timestamp))
                continue;
            _lastSeen[candidate.NormalizedPlate] = timestamp;

            var capture = new NewCapture(
                _campaignId,
                _pointId,
                _jpegEncoder.EncodeJpeg(candidate.PlateCrop, _options.PlateImageJpegQuality),
                candidate.RawText,
                candidate.NormalizedPlate,
                PlateHasher.ComputeHash(candidate.NormalizedPlate, _campaignSalt),
                candidate.Color,
                timestamp);

            stored.Add(await _store.AddAsync(capture, cancellationToken).ConfigureAwait(false));
        }

        return stored;
    }

    private bool IsDuplicate(string normalizedPlate, DateTimeOffset timestamp) =>
        _lastSeen.TryGetValue(normalizedPlate, out var lastSeen) &&
        timestamp - lastSeen < _options.DuplicateSuppressionWindow;
}
