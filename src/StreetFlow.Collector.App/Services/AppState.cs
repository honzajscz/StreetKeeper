using StreetFlow.Collector.Core.Campaign;
using StreetFlow.Collector.Core.Captures;
using StreetFlow.Collector.Core.Geo;
using StreetFlow.Collector.Core.Imaging;

namespace StreetFlow.Collector.App.Services;

/// <summary>
/// Session state shared across pages: the loaded campaign, the GPS-detected
/// boundary point and the active capture session for that point.
/// </summary>
public sealed class AppState
{
    private readonly ICaptureStore _store;
    private readonly IJpegEncoder _jpegEncoder;
    private readonly AlprRuntime _alpr;

    public AppState(ICaptureStore store, IJpegEncoder jpegEncoder, AlprRuntime alpr)
    {
        _store = store;
        _jpegEncoder = jpegEncoder;
        _alpr = alpr;
    }

    public CampaignConfig? Campaign { get; private set; }

    public PointDetectionResult? DetectedPoint { get; private set; }

    public CaptureService? CaptureSession { get; private set; }

    public void SetCampaign(CampaignConfig campaign)
    {
        Campaign = campaign;
        DetectedPoint = null;
        CaptureSession = null;
    }

    /// <summary>Assigns the volunteer to the nearest boundary point of the loaded campaign.</summary>
    public PointDetectionResult? DetectPoint(GeoLocation position)
    {
        if (Campaign is null)
            throw new InvalidOperationException("Load a campaign before detecting the point.");

        DetectedPoint = PointDetector.FindNearestPoint(position, Campaign.Points);
        CaptureSession = null;
        return DetectedPoint;
    }

    /// <summary>Creates the capture session for the detected point; requires loaded ALPR models.</summary>
    public bool TryStartCaptureSession(out string? error)
    {
        error = null;
        if (Campaign is null || DetectedPoint is null)
        {
            error = "Load a campaign and detect your point first.";
            return false;
        }
        if (!_alpr.TryLoad() || _alpr.Pipeline is null)
        {
            error = _alpr.Status;
            return false;
        }

        CaptureSession ??= new CaptureService(
            _alpr.Pipeline,
            _store,
            _jpegEncoder,
            Campaign.CampaignId,
            DetectedPoint.Point.PointId);
        return true;
    }
}
