namespace StreetFlow.Collector.Core.Captures;

/// <summary>
/// Local storage of all captures on the phone (app sandbox) — spec §3.8/§3.9.
/// Holds the sensitive pair {plate crop photo, decoded plate} strictly locally
/// and supports the operator verification actions: invalidate, correct
/// (re-normalize + re-hash), and wipe everything.
/// </summary>
public interface ICaptureStore
{
    Task<CaptureRecord> AddAsync(NewCapture capture, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CaptureRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<CaptureRecord?> GetAsync(string localId, CancellationToken cancellationToken = default);

    /// <summary>Reads the stored plate crop JPEG, or null when the record kept no photo.</summary>
    Task<byte[]?> GetPlateImageAsync(string localId, CancellationToken cancellationToken = default);

    /// <summary>Marks the record invalidated so it is excluded from statistics.</summary>
    Task<CaptureRecord> InvalidateAsync(string localId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an operator correction: the manual transcription goes through
    /// normalization again, the plate hash is recomputed with the campaign salt,
    /// and the record is flagged as corrected.
    /// </summary>
    Task<CaptureRecord> CorrectPlateAsync(string localId, string correctedPlate, string campaignSalt, CancellationToken cancellationToken = default);

    /// <summary>Marks the record's anonymous data as sent.</summary>
    Task<CaptureRecord> MarkSyncedAsync(string localId, CancellationToken cancellationToken = default);

    /// <summary>Wipes the whole local log — all records and all photos (one-button erase, spec §3.9).</summary>
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}
