using StreetFlow.Collector.Core.Campaign;

namespace StreetFlow.Collector.Core.Geo;

/// <summary>Result of assigning a GPS position to a campaign boundary point.</summary>
public sealed record PointDetectionResult(CampaignPoint Point, double DistanceMeters);

/// <summary>
/// Determines which campaign boundary point the volunteer is standing at:
/// the nearest configured point within a given radius of the current GPS position.
/// Pure, fully testable logic (spec §3.2).
/// </summary>
public static class PointDetector
{
    /// <summary>
    /// Default assignment radius. Field calibration of this value is an open
    /// question in the spec (§8); 75 m tolerates ordinary phone-GPS drift while
    /// still separating boundary points of a typical street grid.
    /// </summary>
    public const double DefaultRadiusMeters = 75.0;

    /// <summary>
    /// Finds the nearest campaign point within <paramref name="radiusMeters"/> of
    /// <paramref name="position"/>, or <c>null</c> when no point is close enough.
    /// </summary>
    public static PointDetectionResult? FindNearestPoint(
        GeoLocation position,
        IEnumerable<CampaignPoint> points,
        double radiusMeters = DefaultRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (radiusMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), radiusMeters, "Radius must be positive.");

        PointDetectionResult? best = null;
        foreach (var point in points)
        {
            var distance = GeoMath.DistanceMeters(position, new GeoLocation(point.Lat, point.Lng));
            if (distance <= radiusMeters && (best is null || distance < best.DistanceMeters))
                best = new PointDetectionResult(point, distance);
        }

        return best;
    }
}
