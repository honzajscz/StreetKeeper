using StreetFlow.Collector.Core.Captures;
using StreetFlow.Collector.Core.Plates;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class FileCaptureStoreTests : IDisposable
{
    private static readonly Guid CampaignId = Guid.Parse("8f7c3f2a-1b2c-4d5e-9f0a-112233445566");
    private static readonly string Salt = PlateHasher.DeriveCampaignSalt(CampaignId);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"streetflow-store-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private FileCaptureStore CreateStore() => new(_root);

    private static NewCapture Capture(string raw = "3A2 3456", byte[]? jpeg = null)
    {
        var normalized = PlateNormalizer.Normalize(raw);
        return new NewCapture(
            CampaignId.ToString("D"),
            "north",
            jpeg,
            raw,
            normalized,
            PlateHasher.ComputeHash(normalized, Salt),
            "blue",
            new DateTimeOffset(2026, 6, 16, 8, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task Added_capture_is_persisted_and_survives_reload()
    {
        string localId;
        using (var store = CreateStore())
        {
            var record = await store.AddAsync(Capture(jpeg: [1, 2, 3, 4]));
            localId = record.LocalId;

            Assert.Equal(CaptureStatus.Valid, record.Status);
            Assert.Equal(SyncState.Pending, record.SyncState);
            Assert.Null(record.VehicleType); // iteration 1
            Assert.False(record.IsCorrected);
        }

        // Fresh instance over the same directory — simulates app restart.
        using var reloaded = CreateStore();
        var records = await reloaded.GetAllAsync();
        var loaded = Assert.Single(records);
        Assert.Equal(localId, loaded.LocalId);
        Assert.Equal("3A2 3456", loaded.RawOcrText);
        Assert.Equal("3A23456", loaded.NormalizedPlate);
        Assert.Equal([1, 2, 3, 4], await reloaded.GetPlateImageAsync(localId));
    }

    [Fact]
    public async Task Invalidate_excludes_record_and_flags_synced_records_for_update()
    {
        using var store = CreateStore();
        var neverSynced = await store.AddAsync(Capture("1AA 1111"));
        var alreadySynced = await store.AddAsync(Capture("2BB 2222"));
        await store.MarkSyncedAsync(alreadySynced.LocalId);

        var invalidated = await store.InvalidateAsync(neverSynced.LocalId);
        Assert.Equal(CaptureStatus.Invalidated, invalidated.Status);
        Assert.Equal(SyncState.Pending, invalidated.SyncState); // nothing was sent, nothing to update

        var invalidatedSynced = await store.InvalidateAsync(alreadySynced.LocalId);
        Assert.Equal(CaptureStatus.Invalidated, invalidatedSynced.Status);
        Assert.Equal(SyncState.PendingUpdate, invalidatedSynced.SyncState); // remote copy must be updated
    }

    [Fact]
    public async Task Correction_renormalizes_rehashes_and_marks_the_record()
    {
        using var store = CreateStore();
        var misread = await store.AddAsync(Capture("3AO 1234")); // OCR read O instead of 0
        var originalHash = misread.PlateHash;

        var corrected = await store.CorrectPlateAsync(misread.LocalId, "8E4 8122", Salt);

        Assert.True(corrected.IsCorrected);
        Assert.Equal("8E4 8122", corrected.CorrectedPlate);
        Assert.Equal("3AO 1234", corrected.RawOcrText); // original OCR reading is preserved
        Assert.Equal("8E48122", corrected.NormalizedPlate);
        Assert.Equal(PlateHasher.ComputeHash("8E48122", Salt), corrected.PlateHash);
        Assert.NotEqual(originalHash, corrected.PlateHash);
    }

    [Fact]
    public async Task Correction_of_an_already_synced_record_flags_it_for_update()
    {
        using var store = CreateStore();
        var record = await store.AddAsync(Capture());
        await store.MarkSyncedAsync(record.LocalId);

        var corrected = await store.CorrectPlateAsync(record.LocalId, "7C7 7777", Salt);

        Assert.Equal(SyncState.PendingUpdate, corrected.SyncState);
    }

    [Fact]
    public async Task Correction_to_garbage_is_rejected()
    {
        using var store = CreateStore();
        var record = await store.AddAsync(Capture());

        await Assert.ThrowsAsync<ArgumentException>(() => store.CorrectPlateAsync(record.LocalId, "---", Salt));
    }

    [Fact]
    public async Task DeleteAll_wipes_records_and_photos()
    {
        using var store = CreateStore();
        var record = await store.AddAsync(Capture(jpeg: [9, 9, 9]));

        await store.DeleteAllAsync();

        Assert.Empty(await store.GetAllAsync());
        Assert.Null(await store.GetPlateImageAsync(record.LocalId));
        Assert.Empty(Directory.Exists(_root) ? Directory.GetFiles(_root, "*.jpg", SearchOption.AllDirectories) : []);

        // Store stays usable after the wipe.
        await store.AddAsync(Capture("9ZZ 9999"));
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task Mutating_a_missing_record_throws()
    {
        using var store = CreateStore();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.InvalidateAsync("no-such-id"));
    }
}
