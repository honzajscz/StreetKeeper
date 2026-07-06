namespace StreetFlow.Collector.Core.Campaign;

/// <summary>
/// One boundary point (entry/exit) of a campaign area.
/// </summary>
public sealed record CampaignPoint(
    string PointId,
    string Name,
    double Lat,
    double Lng);

/// <summary>
/// Campaign configuration as authored by the Organizer Console.
/// Contains no personal data. In iteration 1 it is read from a local
/// JSON/mock source (<see cref="LocalJsonCampaignConfigSource"/>); the real
/// Supabase-backed source arrives with the "Supabase schema" surface.
/// <para><paramref name="AreaPolygonGeoJson"/> is the raw GeoJSON of the observed
/// area polygon, kept verbatim — the collector app does not interpret it.</para>
/// </summary>
public sealed record CampaignConfig(
    Guid CampaignId,
    string Name,
    string? AreaPolygonGeoJson,
    IReadOnlyList<CampaignPoint> Points);
