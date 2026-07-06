using System.Text.Json;
using StreetFlow.Collector.Core.Plates;

namespace StreetFlow.Collector.Core.Captures;

/// <summary>
/// File-backed <see cref="ICaptureStore"/> for the app sandbox: an index JSON with
/// all records plus one JPEG per plate crop. Everything stays under a single root
/// directory so a full wipe is a directory-level operation.
/// </summary>
public sealed class FileCaptureStore : ICaptureStore, IDisposable
{
    private const string IndexFileName = "captures.json";
    private const string PlateImageDirectoryName = "plates";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<CaptureRecord>? _records;

    public FileCaptureStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = rootDirectory;
    }

    private string IndexPath => Path.Combine(_rootDirectory, IndexFileName);
    private string PlateImageDirectory => Path.Combine(_rootDirectory, PlateImageDirectoryName);
    private string PlateImagePath(string fileName) => Path.Combine(PlateImageDirectory, fileName);

    public async Task<CaptureRecord> AddAsync(NewCapture capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await LoadAsync(cancellationToken).ConfigureAwait(false);

            var localId = Guid.NewGuid().ToString("D");
            string? imageFile = null;
            if (capture.PlateImageJpeg is { Length: > 0 })
            {
                Directory.CreateDirectory(PlateImageDirectory);
                imageFile = $"{localId}.jpg";
                await File.WriteAllBytesAsync(PlateImagePath(imageFile), capture.PlateImageJpeg, cancellationToken).ConfigureAwait(false);
            }

            var record = new CaptureRecord
            {
                LocalId = localId,
                CampaignId = capture.CampaignId,
                PointId = capture.PointId,
                PlateImageFile = imageFile,
                RawOcrText = capture.RawOcrText,
                NormalizedPlate = capture.NormalizedPlate,
                PlateHash = capture.PlateHash,
                Color = capture.Color,
                VehicleType = null, // iteration 1: vehicle type classification not implemented
                Timestamp = capture.Timestamp,
            };

            records.Add(record);
            await PersistAsync(records, cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<CaptureRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return records.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CaptureRecord?> GetAsync(string localId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return records.FirstOrDefault(r => r.LocalId == localId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<byte[]?> GetPlateImageAsync(string localId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var record = records.FirstOrDefault(r => r.LocalId == localId);
            if (record?.PlateImageFile is null)
                return null;

            var path = PlateImagePath(record.PlateImageFile);
            return File.Exists(path)
                ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<CaptureRecord> InvalidateAsync(string localId, CancellationToken cancellationToken = default) =>
        MutateAsync(localId, record =>
        {
            record.Status = CaptureStatus.Invalidated;
            if (record.SyncState == SyncState.Synced)
                record.SyncState = SyncState.PendingUpdate; // remote copy must be invalidated too
        }, cancellationToken);

    public Task<CaptureRecord> CorrectPlateAsync(string localId, string correctedPlate, string campaignSalt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correctedPlate);
        ArgumentException.ThrowIfNullOrEmpty(campaignSalt);

        return MutateAsync(localId, record =>
        {
            // Operator correction re-runs the exact same normalize→hash chain as capture (spec §3.6/§3.9).
            var normalized = PlateNormalizer.Normalize(correctedPlate);
            if (normalized.Length == 0)
                throw new ArgumentException($"Corrected plate '{correctedPlate}' contains no plate characters.", nameof(correctedPlate));

            record.CorrectedPlate = correctedPlate;
            record.NormalizedPlate = normalized;
            record.PlateHash = PlateHasher.ComputeHash(normalized, campaignSalt);
            record.IsCorrected = true;
            if (record.SyncState == SyncState.Synced)
                record.SyncState = SyncState.PendingUpdate; // remote copy holds the old hash
        }, cancellationToken);
    }

    public Task<CaptureRecord> MarkSyncedAsync(string localId, CancellationToken cancellationToken = default) =>
        MutateAsync(localId, record => record.SyncState = SyncState.Synced, cancellationToken);

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(PlateImageDirectory))
                Directory.Delete(PlateImageDirectory, recursive: true);
            if (File.Exists(IndexPath))
                File.Delete(IndexPath);
            _records = [];
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task<CaptureRecord> MutateAsync(string localId, Action<CaptureRecord> mutation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localId);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var record = records.FirstOrDefault(r => r.LocalId == localId)
                ?? throw new KeyNotFoundException($"Capture '{localId}' not found in the local store.");

            mutation(record);
            await PersistAsync(records, cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<CaptureRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_records is not null)
            return _records;

        if (!File.Exists(IndexPath))
        {
            _records = [];
            return _records;
        }

        await using var stream = File.OpenRead(IndexPath);
        _records = await JsonSerializer.DeserializeAsync<List<CaptureRecord>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        return _records;
    }

    private async Task PersistAsync(List<CaptureRecord> records, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootDirectory);
        var tempPath = IndexPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        File.Move(tempPath, IndexPath, overwrite: true);
        _records = records;
    }
}
