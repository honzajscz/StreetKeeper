using StreetFlow.Collector.Core.Captures;

namespace StreetFlow.Collector.Core.Sync;

/// <summary>
/// Syncs the local capture store to a record sink (spec §3.10): not fire-and-forget —
/// records first live in the store, then their anonymous projection is submitted and
/// the record is marked synced. Records the operator invalidated before they were
/// ever sent are never submitted. Records in <see cref="SyncState.PendingUpdate"/>
/// (corrected/invalidated after sending) are left for the real Supabase sync logic
/// of a later surface.
/// </summary>
public sealed class CaptureSyncService
{
    private readonly ICaptureStore _store;
    private readonly IRecordSink _sink;

    public CaptureSyncService(ICaptureStore store, IRecordSink sink)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>Submits all valid, not-yet-sent captures. Returns how many records were submitted.</summary>
    public async Task<int> SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        var records = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var submitted = 0;

        foreach (var record in records)
        {
            if (record.Status != CaptureStatus.Valid || record.SyncState != SyncState.Pending)
                continue;

            await _sink.SubmitAsync(AnonymousRecord.FromCapture(record), cancellationToken).ConfigureAwait(false);
            await _store.MarkSyncedAsync(record.LocalId, cancellationToken).ConfigureAwait(false);
            submitted++;
        }

        return submitted;
    }
}
