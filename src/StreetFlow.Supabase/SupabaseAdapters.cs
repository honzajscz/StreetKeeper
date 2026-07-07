using StreetFlow.Collector.Core.Campaign;
using StreetFlow.Collector.Core.Sync;

namespace StreetFlow.Supabase;

/// <summary>
/// The real campaign config source of the PRD: campaigns authored by the
/// Organizer Console, read from the shared database. Replaces the iteration-1
/// local JSON mock in the collector app when Supabase is configured.
/// </summary>
public sealed class SupabaseCampaignConfigSource(SupabaseClient client) : ICampaignConfigSource
{
    public Task<CampaignConfig?> GetConfigAsync(Guid campaignId, CancellationToken cancellationToken = default) =>
        client.GetCampaignAsync(campaignId, cancellationToken);
}

/// <summary>
/// The real record sink of the PRD: anonymous records go to the shared
/// database. Replaces the iteration-1 stub sink when Supabase is configured.
/// </summary>
public sealed class SupabaseRecordSink(SupabaseClient client) : IRecordSink
{
    public Task SubmitAsync(AnonymousRecord record, CancellationToken cancellationToken = default) =>
        client.SubmitRecordAsync(record, cancellationToken);
}
