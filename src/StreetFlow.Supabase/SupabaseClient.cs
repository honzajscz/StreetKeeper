using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StreetFlow.Collector.Core.Campaign;
using StreetFlow.Collector.Core.Sync;

namespace StreetFlow.Supabase;

/// <summary>
/// Thin client for the StreetFlow tables behind Supabase's auto-generated REST
/// API (PostgREST). Deliberately dumb: the row shapes are exactly the PRD wire
/// shapes, so campaign rows are parsed by the same <see cref="CampaignConfigJson"/>
/// code every other surface uses.
/// </summary>
public sealed class SupabaseClient
{
    private const int RecordPageSize = 1000;

    private readonly HttpClient _http;
    private readonly SupabaseOptions _options;
    private readonly Uri _restBase;

    public SupabaseClient(HttpClient http, SupabaseOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        _restBase = new Uri(options.Url.TrimEnd('/') + "/rest/v1/");
    }

    // -- campaigns ----------------------------------------------------------

    public async Task<CampaignConfig?> GetCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"campaigns?campaign_id=eq.{campaignId:D}&select=campaign_id,name,area_polygon,points");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
            return null;

        // Row shape == PRD wire shape → reuse the shared parser.
        return CampaignConfigJson.Parse(rows[0].GetRawText());
    }

    public async Task SaveCampaignAsync(CampaignConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var points = new JsonArray();
        foreach (var point in config.Points)
        {
            points.Add(new JsonObject
            {
                ["point_id"] = point.PointId,
                ["name"] = point.Name,
                ["lat"] = point.Lat,
                ["lng"] = point.Lng,
            });
        }

        var row = new JsonObject
        {
            ["campaign_id"] = config.CampaignId.ToString("D"),
            ["name"] = config.Name,
            ["area_polygon"] = config.AreaPolygonGeoJson is null ? null : JsonNode.Parse(config.AreaPolygonGeoJson),
            ["points"] = points,
        };

        using var request = CreateRequest(HttpMethod.Post, "campaigns");
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        request.Content = new StringContent(row.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // -- records ------------------------------------------------------------

    public async Task SubmitRecordAsync(AnonymousRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var request = CreateRequest(HttpMethod.Post, "records");
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        request.Content = JsonContent.Create(record);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads all anonymous records of a campaign, oldest first (paged under the hood).</summary>
    public async Task<IReadOnlyList<AnonymousRecord>> GetRecordsAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var all = new List<AnonymousRecord>();
        for (var offset = 0; ; offset += RecordPageSize)
        {
            using var request = CreateRequest(HttpMethod.Get,
                $"records?campaign=eq.{campaignId:D}&select=campaign,point_id,plate_hash,color,vehicle_type,timestamp" +
                $"&order=timestamp.asc&limit={RecordPageSize}&offset={offset}");
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

            var page = await response.Content.ReadFromJsonAsync<List<AnonymousRecord>>(cancellationToken).ConfigureAwait(false) ?? [];
            all.AddRange(page);
            if (page.Count < RecordPageSize)
                return all;
        }
    }

    // -- plumbing -----------------------------------------------------------

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method, new Uri(_restBase, relativeUrl));
        request.Headers.TryAddWithoutValidation("apikey", _options.AnonKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.AnonKey}");
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.Dispose();
            throw new SupabaseApiException(
                $"Supabase request {request.Method} {request.RequestUri!.PathAndQuery} failed with {(int)response.StatusCode}: " +
                body[..Math.Min(body.Length, 500)]);
        }
        return response;
    }
}

/// <summary>A Supabase REST call returned a non-success status.</summary>
public sealed class SupabaseApiException(string message) : Exception(message);
