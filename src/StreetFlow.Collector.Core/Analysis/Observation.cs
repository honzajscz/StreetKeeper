using StreetFlow.Collector.Core.Sync;

namespace StreetFlow.Collector.Core.Analysis;

/// <summary>
/// One anonymous sighting of a vehicle at a boundary point — the input of transit
/// analysis (PRD §5). Carries exactly the fields of the anonymous wire record
/// that matter for pairing; the caller is expected to feed observations of a
/// single campaign.
/// </summary>
public sealed record Observation(
    string PlateHash,
    string PointId,
    string? Color,
    string? VehicleType,
    DateTimeOffset Timestamp)
{
    /// <summary>Projects an anonymous wire record (e.g. read back from Supabase) to an observation.</summary>
    public static Observation FromAnonymousRecord(AnonymousRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new Observation(record.PlateHash, record.PointId, record.Color, record.VehicleType, record.Timestamp);
    }
}
