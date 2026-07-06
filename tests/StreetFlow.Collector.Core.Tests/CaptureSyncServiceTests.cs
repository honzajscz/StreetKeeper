using StreetFlow.Collector.Core.Captures;
using StreetFlow.Collector.Core.Plates;
using StreetFlow.Collector.Core.Sync;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class CaptureSyncServiceTests : IDisposable
{
    private static readonly Guid CampaignId = Guid.Parse("8f7c3f2a-1b2c-4d5e-9f0a-112233445566");
    private static readonly string Salt = PlateHasher.DeriveCampaignSalt(CampaignId);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"streetflow-sync-{Guid.NewGuid():N}");
    private readonly FileCaptureStore _store;
    private readonly StubRecordSink _sink = new();
    private readonly CaptureSyncService _service;

    public CaptureSyncServiceTests()
    {
        _store = new FileCaptureStore(_root);
        _service = new CaptureSyncService(_store, _sink);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private Task<CaptureRecord> AddCaptureAsync(string raw = "3A2 3456")
    {
        var normalized = PlateNormalizer.Normalize(raw);
        return _store.AddAsync(new NewCapture(
            CampaignId.ToString("D"),
            "north",
            [1, 2, 3], // plate crop photo — must never reach the sink
            raw,
            normalized,
            PlateHasher.ComputeHash(normalized, Salt),
            "red",
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Sync_submits_only_the_anonymous_projection()
    {
        var record = await AddCaptureAsync("3A2 3456");

        var count = await _service.SyncPendingAsync();

        Assert.Equal(1, count);
        var submitted = Assert.Single(_sink.Submitted);
        Assert.Equal(record.CampaignId, submitted.Campaign);
        Assert.Equal("north", submitted.PointId);
        Assert.Equal(record.PlateHash, submitted.PlateHash);
        Assert.Equal("red", submitted.Color);
        Assert.Null(submitted.VehicleType);
        Assert.Equal(record.Timestamp, submitted.Timestamp);

        // The privacy boundary is structural: the wire type has no members that
        // could carry the photo or the plate text.
        var wireProperties = typeof(AnonymousRecord).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("RawOcrText", wireProperties);
        Assert.DoesNotContain("NormalizedPlate", wireProperties);
        Assert.DoesNotContain("CorrectedPlate", wireProperties);
        Assert.DoesNotContain("PlateImageFile", wireProperties);
    }

    [Fact]
    public async Task Synced_records_are_marked_and_not_resubmitted()
    {
        await AddCaptureAsync();

        Assert.Equal(1, await _service.SyncPendingAsync());
        Assert.Equal(0, await _service.SyncPendingAsync());
        Assert.Single(_sink.Submitted);

        var record = Assert.Single(await _store.GetAllAsync());
        Assert.Equal(SyncState.Synced, record.SyncState);
    }

    [Fact]
    public async Task Invalidated_records_are_never_submitted()
    {
        var record = await AddCaptureAsync();
        await _store.InvalidateAsync(record.LocalId);

        Assert.Equal(0, await _service.SyncPendingAsync());
        Assert.Empty(_sink.Submitted);
    }

    [Fact]
    public async Task Records_corrected_after_sync_are_left_for_the_update_logic_of_a_later_surface()
    {
        var record = await AddCaptureAsync();
        await _service.SyncPendingAsync();
        await _store.CorrectPlateAsync(record.LocalId, "8E4 8122", Salt);

        Assert.Equal(0, await _service.SyncPendingAsync()); // PendingUpdate is not re-submitted in iteration 1
        Assert.Single(_sink.Submitted);
        Assert.Equal(SyncState.PendingUpdate, (await _store.GetAsync(record.LocalId))!.SyncState);
    }
}
