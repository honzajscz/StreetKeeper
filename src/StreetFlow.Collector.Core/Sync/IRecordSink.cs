namespace StreetFlow.Collector.Core.Sync;

/// <summary>
/// Destination for anonymous records (spec §3.10). Iteration 1 ships only
/// <see cref="StubRecordSink"/>; the real Supabase sink (including
/// update/invalidation of already-sent records) arrives with the
/// "Supabase schema" surface.
/// </summary>
public interface IRecordSink
{
    Task SubmitAsync(AnonymousRecord record, CancellationToken cancellationToken = default);
}

/// <summary>
/// Iteration-1 stub sink: accepts every record and remembers it in memory so the
/// UI and tests can inspect what would have been sent.
/// </summary>
public sealed class StubRecordSink : IRecordSink
{
    private readonly List<AnonymousRecord> _submitted = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<AnonymousRecord> Submitted
    {
        get { lock (_gate) return _submitted.ToList(); }
    }

    public Task SubmitAsync(AnonymousRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate) _submitted.Add(record);
        return Task.CompletedTask;
    }
}
