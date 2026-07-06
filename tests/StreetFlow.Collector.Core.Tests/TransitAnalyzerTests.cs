using StreetFlow.Collector.Core.Analysis;
using StreetFlow.Collector.Core.Sync;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class TransitAnalyzerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 16, 8, 0, 0, TimeSpan.FromHours(2));

    private static Observation At(string hash, string point, int minutesAfterT0, string? color = "blue", string? type = null) =>
        new(hash, point, color, type, T0.AddMinutes(minutesAfterT0));

    [Fact]
    public void Same_hash_at_two_points_within_window_is_transit()
    {
        var result = TransitAnalyzer.Analyze([
            At("h1", "north", 0),
            At("h1", "south", 2),
        ]);

        var passage = Assert.Single(result.Transits);
        Assert.Equal("north", passage.EntryPointId);
        Assert.Equal("south", passage.ExitPointId);
        Assert.Equal(TimeSpan.FromMinutes(2), passage.Duration);
        Assert.Empty(result.Locals);
        Assert.Equal(1.0, result.TransitShare);
    }

    [Fact]
    public void Hash_seen_at_one_point_only_is_local()
    {
        var result = TransitAnalyzer.Analyze([At("h1", "north", 0)]);

        Assert.Empty(result.Transits);
        var local = Assert.Single(result.Locals);
        Assert.Equal("north", local.PointId);
        Assert.Equal(0.0, result.TransitShare);
    }

    [Fact]
    public void Reappearance_after_many_hours_is_local_not_transit()
    {
        // PRD §5: a car that exits only hours later parked inside — local destination.
        var result = TransitAnalyzer.Analyze([
            At("h1", "north", 0),
            At("h1", "south", 4 * 60),
        ]);

        Assert.Empty(result.Transits);
        Assert.Equal(2, result.LocalCount);
        Assert.Equal(0.0, result.TransitShare);
    }

    [Fact]
    public void Window_boundary_is_inclusive()
    {
        var options = new TransitAnalysisOptions { TransitWindow = TimeSpan.FromMinutes(5) };

        var atBoundary = TransitAnalyzer.Analyze([At("h1", "north", 0), At("h1", "south", 5)], options);
        Assert.Single(atBoundary.Transits);

        var beyond = TransitAnalyzer.Analyze([At("h1", "north", 0), At("h1", "south", 6)], options);
        Assert.Empty(beyond.Transits);
    }

    [Fact]
    public void Contradicting_color_blocks_the_pair_and_is_reported()
    {
        // Two different cars whose misread plates collide on one hash must not
        // count as transit — the fingerprint safeguard of PRD §5.
        var result = TransitAnalyzer.Analyze([
            At("h1", "north", 0, color: "red"),
            At("h1", "south", 2, color: "blue"),
        ]);

        Assert.Empty(result.Transits);
        Assert.Equal(2, result.LocalCount);
        Assert.Equal(1, result.FingerprintRejectedPairs);
    }

    [Fact]
    public void Missing_color_or_type_never_blocks_a_match()
    {
        var result = TransitAnalyzer.Analyze([
            At("h1", "north", 0, color: null, type: null),
            At("h1", "south", 2, color: "blue", type: "van"),
        ]);

        Assert.Single(result.Transits);
        Assert.Equal(0, result.FingerprintRejectedPairs);
    }

    [Fact]
    public void Fingerprint_check_can_be_disabled()
    {
        var result = TransitAnalyzer.Analyze(
            [At("h1", "north", 0, color: "red"), At("h1", "south", 2, color: "blue")],
            new TransitAnalysisOptions { UseVehicleFingerprint = false });

        Assert.Single(result.Transits);
    }

    [Fact]
    public void Same_point_twice_within_window_is_not_transit()
    {
        var result = TransitAnalyzer.Analyze([
            At("h1", "north", 0),
            At("h1", "north", 2),
        ]);

        Assert.Empty(result.Transits);
        Assert.Equal(2, result.LocalCount);
    }

    [Fact]
    public void One_car_passing_twice_yields_two_passages()
    {
        // Morning A→B, evening B→A.
        var result = TransitAnalyzer.Analyze([
            At("h1", "north", 0),
            At("h1", "south", 3),
            At("h1", "south", 10 * 60),
            At("h1", "north", 10 * 60 + 4),
        ]);

        Assert.Equal(2, result.TransitCount);
        Assert.Empty(result.Locals);
        Assert.Equal("south", result.Transits[1].EntryPointId);
    }

    [Fact]
    public void Different_hashes_are_analyzed_independently()
    {
        var result = TransitAnalyzer.Analyze([
            At("transit-car", "north", 0),
            At("parked-car", "north", 1),
            At("transit-car", "south", 3),
        ]);

        Assert.Equal("transit-car", Assert.Single(result.Transits).PlateHash);
        Assert.Equal("parked-car", Assert.Single(result.Locals).PlateHash);
        Assert.Equal(0.5, result.TransitShare);
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var shuffled = TransitAnalyzer.Analyze([
            At("h1", "south", 2),
            At("h1", "north", 0),
        ]);

        var passage = Assert.Single(shuffled.Transits);
        Assert.Equal("north", passage.EntryPointId); // earliest sighting is the entry
    }

    [Fact]
    public void Empty_input_yields_no_share()
    {
        var result = TransitAnalyzer.Analyze([]);

        Assert.Equal(0, result.TransitCount);
        Assert.Equal(0, result.LocalCount);
        Assert.Null(result.TransitShare);
    }

    [Fact]
    public void Headline_share_matches_prd_formula()
    {
        // 2 transits + 1 local → 2 / (2 + 1).
        var result = TransitAnalyzer.Analyze([
            At("car-a", "north", 0), At("car-a", "south", 2),
            At("car-b", "north", 5), At("car-b", "south", 8),
            At("car-c", "north", 6),
        ]);

        Assert.Equal(2, result.TransitCount);
        Assert.Equal(1, result.LocalCount);
        Assert.Equal(2.0 / 3.0, result.TransitShare!.Value, precision: 10);
    }

    [Fact]
    public void Observations_can_be_built_from_anonymous_records()
    {
        var record = new AnonymousRecord("campaign", "north", "hash", "red", null, T0);
        var observation = Observation.FromAnonymousRecord(record);

        Assert.Equal("hash", observation.PlateHash);
        Assert.Equal("north", observation.PointId);
        Assert.Equal("red", observation.Color);
        Assert.Null(observation.VehicleType);
        Assert.Equal(T0, observation.Timestamp);
    }
}
