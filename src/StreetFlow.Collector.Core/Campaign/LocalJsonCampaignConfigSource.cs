namespace StreetFlow.Collector.Core.Campaign;

/// <summary>
/// Iteration-1 campaign config source: reads campaign configuration from a local
/// JSON document (a single campaign object or an array of campaigns) supplied by
/// a delegate — typically a bundled mock file on the phone.
/// </summary>
public sealed class LocalJsonCampaignConfigSource : ICampaignConfigSource
{
    private readonly Func<CancellationToken, Task<string>> _jsonProvider;

    /// <param name="jsonProvider">Returns the raw JSON document (e.g. reads a bundled asset or a file).</param>
    public LocalJsonCampaignConfigSource(Func<CancellationToken, Task<string>> jsonProvider)
    {
        _jsonProvider = jsonProvider ?? throw new ArgumentNullException(nameof(jsonProvider));
    }

    /// <summary>Creates a source reading from a file on disk.</summary>
    public static LocalJsonCampaignConfigSource FromFile(string path) =>
        new(async ct => await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));

    /// <summary>Creates a source over an in-memory JSON string (useful for tests and demos).</summary>
    public static LocalJsonCampaignConfigSource FromJson(string json) =>
        new(_ => Task.FromResult(json));

    public async Task<CampaignConfig?> GetConfigAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var json = await _jsonProvider(cancellationToken).ConfigureAwait(false);
        var campaigns = CampaignConfigJson.ParseMany(json);
        return campaigns.FirstOrDefault(c => c.CampaignId == campaignId);
    }
}
