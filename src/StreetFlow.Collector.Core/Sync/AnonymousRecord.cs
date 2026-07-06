using System.Text.Json.Serialization;
using StreetFlow.Collector.Core.Captures;

namespace StreetFlow.Collector.Core.Sync;

/// <summary>
/// The ONLY payload that ever leaves the phone (spec §4). Deliberately has no
/// field for the photo or the raw plate text — the privacy boundary is enforced
/// by the shape of this type.
/// </summary>
public sealed record AnonymousRecord(
    [property: JsonPropertyName("campaign")] string Campaign,
    [property: JsonPropertyName("point_id")] string PointId,
    [property: JsonPropertyName("plate_hash")] string PlateHash,
    [property: JsonPropertyName("color")] string? Color,
    [property: JsonPropertyName("vehicle_type")] string? VehicleType,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp)
{
    /// <summary>Projects a local capture to its anonymous wire form.</summary>
    public static AnonymousRecord FromCapture(CaptureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new AnonymousRecord(
            record.CampaignId,
            record.PointId,
            record.PlateHash,
            record.Color,
            record.VehicleType,
            record.Timestamp);
    }
}
