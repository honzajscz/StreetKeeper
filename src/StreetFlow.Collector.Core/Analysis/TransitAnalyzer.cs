namespace StreetFlow.Collector.Core.Analysis;

public sealed class TransitAnalysisOptions
{
    /// <summary>
    /// The time window X of PRD §5: the same plate at two different boundary
    /// points within this window counts as transit. The spec says to start at
    /// 3–5 minutes and calibrate from pilot data; the default takes the upper
    /// bound so the first pilot doesn't silently drop slow crossings.
    /// </summary>
    public TimeSpan TransitWindow { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether the vehicle fingerprint (color + type) must be compatible for a
    /// hash match to count as transit — the safeguard of PRD §5 against OCR
    /// misreads mapping two different cars onto one hash. A missing value on
    /// either side never blocks a match; only a contradiction does.
    /// </summary>
    public bool UseVehicleFingerprint { get; init; } = true;
}

/// <summary>One proven pass through the area: entry at one point, exit at another within the window.</summary>
public sealed record TransitPassage(
    string PlateHash,
    string EntryPointId,
    string ExitPointId,
    DateTimeOffset EnteredAt,
    DateTimeOffset ExitedAt)
{
    public TimeSpan Duration => ExitedAt - EnteredAt;
}

/// <summary>A sighting that never paired up — the vehicle most likely had a destination in the area.</summary>
public sealed record LocalObservation(string PlateHash, string PointId, DateTimeOffset Timestamp);

/// <summary>Result of transit analysis over one campaign's observations.</summary>
public sealed record TransitAnalysisResult(
    IReadOnlyList<TransitPassage> Transits,
    IReadOnlyList<LocalObservation> Locals,
    int FingerprintRejectedPairs)
{
    public int TransitCount => Transits.Count;

    public int LocalCount => Locals.Count;

    /// <summary>
    /// The headline metric of the PRD: transit / (transit + local),
    /// or <c>null</c> when there is no data to speak of.
    /// </summary>
    public double? TransitShare =>
        TransitCount + LocalCount == 0 ? null : (double)TransitCount / (TransitCount + LocalCount);
}

/// <summary>
/// The proof logic of PRD §5, as a pure function over anonymous observations:
/// pairs sightings of the same <c>plate_hash</c> at two different boundary
/// points within the time window X into transit passages (hash match confirmed
/// by the color/type fingerprint); everything that never pairs is a local
/// observation. Unmatched sightings count as local rather than being discarded,
/// which biases the headline number *downwards* — the defensible direction for
/// a public claim.
/// <para>
/// This lives in Core so the Public Dashboard (Blazor WASM) computes the
/// statistics in the browser from the exact same code the pilot analysis uses.
/// </para>
/// </summary>
public static class TransitAnalyzer
{
    public static TransitAnalysisResult Analyze(
        IEnumerable<Observation> observations,
        TransitAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(observations);
        options ??= new TransitAnalysisOptions();

        var transits = new List<TransitPassage>();
        var locals = new List<LocalObservation>();
        var fingerprintRejected = 0;

        foreach (var group in observations.GroupBy(o => o.PlateHash))
        {
            // Greedy in-order pairing per vehicle: the earliest unmatched sighting
            // is either closed by the next sighting at another point inside the
            // window, or settles as a local observation.
            Observation? pending = null;

            foreach (var current in group.OrderBy(o => o.Timestamp))
            {
                if (pending is null)
                {
                    pending = current;
                    continue;
                }

                var withinWindow = current.Timestamp - pending.Timestamp <= options.TransitWindow;
                if (withinWindow && current.PointId != pending.PointId)
                {
                    if (!options.UseVehicleFingerprint || FingerprintCompatible(pending, current))
                    {
                        transits.Add(new TransitPassage(
                            group.Key, pending.PointId, current.PointId, pending.Timestamp, current.Timestamp));
                        pending = null;
                        continue;
                    }
                    fingerprintRejected++;
                }

                // Same point again, window expired, or fingerprint contradiction:
                // the earlier sighting settles as local, the newer one keeps waiting.
                locals.Add(ToLocal(pending));
                pending = current;
            }

            if (pending is not null)
                locals.Add(ToLocal(pending));
        }

        return new TransitAnalysisResult(transits, locals, fingerprintRejected);
    }

    /// <summary>
    /// The fingerprint is a safeguard, not a filter: only a direct contradiction
    /// (both sides known and different) blocks a match.
    /// </summary>
    private static bool FingerprintCompatible(Observation a, Observation b) =>
        AttributeCompatible(a.Color, b.Color) && AttributeCompatible(a.VehicleType, b.VehicleType);

    private static bool AttributeCompatible(string? a, string? b) =>
        string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static LocalObservation ToLocal(Observation observation) =>
        new(observation.PlateHash, observation.PointId, observation.Timestamp);
}
