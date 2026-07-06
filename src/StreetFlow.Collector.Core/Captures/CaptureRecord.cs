using System.Text.Json.Serialization;

namespace StreetFlow.Collector.Core.Captures;

public enum CaptureStatus
{
    /// <summary>Counts toward statistics.</summary>
    Valid,
    /// <summary>Excluded from statistics by the operator (spec §3.9).</summary>
    Invalidated,
}

public enum SyncState
{
    /// <summary>Not yet sent to Supabase.</summary>
    Pending,
    /// <summary>The anonymous record was sent.</summary>
    Synced,
    /// <summary>Sent, then corrected/invalidated locally — the remote copy needs an update
    /// (handled by the real Supabase sync in a later surface; tracked already in iteration 1).</summary>
    PendingUpdate,
}

/// <summary>
/// One capture in the local store (spec §3.8). The plate crop photo and the raw
/// OCR text live only inside this store, i.e. only on the phone — they are never
/// part of the anonymous record that leaves the device (§4).
/// </summary>
public sealed class CaptureRecord
{
    [JsonPropertyName("local_id")]
    public required string LocalId { get; init; }

    [JsonPropertyName("campaign_id")]
    public required string CampaignId { get; init; }

    [JsonPropertyName("point_id")]
    public required string PointId { get; init; }

    /// <summary>File name of the plate crop JPEG inside the store, or null when no photo was kept.</summary>
    [JsonPropertyName("plate_image_file")]
    public string? PlateImageFile { get; set; }

    /// <summary>What OCR originally read — retained for operator verification.</summary>
    [JsonPropertyName("raw_ocr_text")]
    public required string RawOcrText { get; init; }

    /// <summary>Operator's manual transcription, when the record was corrected.</summary>
    [JsonPropertyName("corrected_plate")]
    public string? CorrectedPlate { get; set; }

    [JsonPropertyName("normalized_plate")]
    public required string NormalizedPlate { get; set; }

    [JsonPropertyName("plate_hash")]
    public required string PlateHash { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; init; }

    /// <summary>Always null in iteration 1; vehicle-type classification is a later iteration.</summary>
    [JsonPropertyName("vehicle_type")]
    public string? VehicleType { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter<CaptureStatus>))]
    public CaptureStatus Status { get; set; } = CaptureStatus.Valid;

    [JsonPropertyName("sync_state")]
    [JsonConverter(typeof(JsonStringEnumConverter<SyncState>))]
    public SyncState SyncState { get; set; } = SyncState.Pending;

    [JsonPropertyName("is_corrected")]
    public bool IsCorrected { get; set; }
}

/// <summary>Payload for adding a fresh capture to the store.</summary>
public sealed record NewCapture(
    string CampaignId,
    string PointId,
    byte[]? PlateImageJpeg,
    string RawOcrText,
    string NormalizedPlate,
    string PlateHash,
    string? Color,
    DateTimeOffset Timestamp);
