namespace StreetFlow.Collector.Core.Campaign;

/// <summary>
/// Provides campaign configuration by <c>campaign_id</c>.
/// Iteration 1 ships a local JSON/mock implementation; the real Supabase-backed
/// source arrives with the "Supabase schema" surface.
/// </summary>
public interface ICampaignConfigSource
{
    /// <returns>The campaign configuration, or <c>null</c> when the campaign is unknown.</returns>
    Task<CampaignConfig?> GetConfigAsync(Guid campaignId, CancellationToken cancellationToken = default);
}
