namespace StreetFlow.Collector.Core.Geo;

public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_000.0;

    /// <summary>Great-circle distance between two WGS-84 coordinates (haversine formula).</summary>
    public static double DistanceMeters(GeoLocation a, GeoLocation b)
    {
        var latA = ToRadians(a.Lat);
        var latB = ToRadians(b.Lat);
        var dLat = ToRadians(b.Lat - a.Lat);
        var dLng = ToRadians(b.Lng - a.Lng);

        var sinLat = Math.Sin(dLat / 2);
        var sinLng = Math.Sin(dLng / 2);
        var h = sinLat * sinLat + Math.Cos(latA) * Math.Cos(latB) * sinLng * sinLng;
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
