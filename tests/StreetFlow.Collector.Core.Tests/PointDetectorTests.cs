using StreetFlow.Collector.Core.Campaign;
using StreetFlow.Collector.Core.Geo;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class PointDetectorTests
{
    // Two boundary points a few hundred meters apart in Prague.
    private static readonly CampaignPoint North = new("north", "sever — vjezd", 50.0910, 14.4200);
    private static readonly CampaignPoint South = new("south", "jih — výjezd", 50.0880, 14.4200);

    [Fact]
    public void Volunteer_standing_at_a_point_is_assigned_to_it()
    {
        // ~11 m east of the north point.
        var position = new GeoLocation(50.0910, 14.42015);

        var result = PointDetector.FindNearestPoint(position, [North, South], radiusMeters: 75);

        Assert.NotNull(result);
        Assert.Equal("north", result.Point.PointId);
        Assert.InRange(result.DistanceMeters, 0, 30);
    }

    [Fact]
    public void Picks_the_nearest_of_multiple_points_in_radius()
    {
        // Slightly north of the midpoint between the two points (~150 m apart each).
        var position = new GeoLocation(50.0896, 14.4200);

        var result = PointDetector.FindNearestPoint(position, [North, South], radiusMeters: 500);

        Assert.NotNull(result);
        Assert.Equal("north", result.Point.PointId);
    }

    [Fact]
    public void Position_outside_radius_of_every_point_yields_null()
    {
        var farAway = new GeoLocation(50.2000, 14.4200); // ~12 km north

        Assert.Null(PointDetector.FindNearestPoint(farAway, [North, South], radiusMeters: 75));
    }

    [Fact]
    public void Empty_point_list_yields_null()
    {
        Assert.Null(PointDetector.FindNearestPoint(new GeoLocation(50.0910, 14.4200), []));
    }

    [Fact]
    public void Non_positive_radius_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PointDetector.FindNearestPoint(new GeoLocation(0, 0), [North], radiusMeters: 0));
    }

    [Fact]
    public void Haversine_distance_matches_known_reference()
    {
        // Prague (50.0755, 14.4378) → Brno (49.1951, 16.6068) is ≈ 185 km great-circle.
        var distance = GeoMath.DistanceMeters(new GeoLocation(50.0755, 14.4378), new GeoLocation(49.1951, 16.6068));
        Assert.InRange(distance, 180_000, 190_000);
    }

    [Fact]
    public void Distance_to_self_is_zero()
    {
        var here = new GeoLocation(50.0910, 14.4200);
        Assert.Equal(0, GeoMath.DistanceMeters(here, here), precision: 6);
    }
}
