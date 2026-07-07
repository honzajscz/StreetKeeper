using System.Net;
using System.Text;
using System.Text.Json;
using StreetFlow.Collector.Core.Campaign;
using StreetFlow.Collector.Core.Sync;
using StreetFlow.Supabase;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class SupabaseClientTests
{
    private static readonly Guid CampaignId = Guid.Parse("8f7c3f2a-1b2c-4d5e-9f0a-112233445566");

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];
        public Queue<HttpResponseMessage> Responses { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return Responses.Count > 0
                ? Responses.Dequeue()
                : Json("[]");
        }

        public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static (SupabaseClient Client, FakeHandler Handler) CreateClient()
    {
        var handler = new FakeHandler();
        var client = new SupabaseClient(
            new HttpClient(handler),
            new SupabaseOptions { Url = "https://example.supabase.co", AnonKey = "anon-key-123" });
        return (client, handler);
    }

    [Fact]
    public async Task GetCampaign_queries_by_id_with_auth_headers_and_parses_prd_shape()
    {
        var (client, handler) = CreateClient();
        handler.Responses.Enqueue(FakeHandler.Json("""
            [{
              "campaign_id": "8f7c3f2a-1b2c-4d5e-9f0a-112233445566",
              "name": "Pilot",
              "area_polygon": { "type": "Polygon", "coordinates": [] },
              "points": [ { "point_id": "north", "name": "sever", "lat": 50.09, "lng": 14.42 } ]
            }]
            """));

        var campaign = await client.GetCampaignAsync(CampaignId);

        var (request, _) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith("https://example.supabase.co/rest/v1/campaigns?campaign_id=eq.8f7c3f2a-", request.RequestUri!.ToString());
        Assert.Equal("anon-key-123", Assert.Single(request.Headers.GetValues("apikey")));
        Assert.Equal("Bearer anon-key-123", Assert.Single(request.Headers.GetValues("Authorization")));

        Assert.NotNull(campaign);
        Assert.Equal("Pilot", campaign.Name);
        Assert.Equal("north", Assert.Single(campaign.Points).PointId);
    }

    [Fact]
    public async Task GetCampaign_returns_null_when_no_row_matches()
    {
        var (client, handler) = CreateClient();
        handler.Responses.Enqueue(FakeHandler.Json("[]"));

        Assert.Null(await client.GetCampaignAsync(CampaignId));
    }

    [Fact]
    public async Task SaveCampaign_posts_the_prd_wire_shape()
    {
        var (client, handler) = CreateClient();
        handler.Responses.Enqueue(FakeHandler.Json("", HttpStatusCode.Created));

        await client.SaveCampaignAsync(new CampaignConfig(
            CampaignId,
            "Pilot",
            """{"type":"Polygon","coordinates":[]}""",
            [new CampaignPoint("north", "sever", 50.09, 14.42)]));

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/rest/v1/campaigns", request.RequestUri!.ToString());
        Assert.Equal("return=minimal", Assert.Single(request.Headers.GetValues("Prefer")));

        using var payload = JsonDocument.Parse(body!);
        var root = payload.RootElement;
        Assert.Equal(CampaignId.ToString("D"), root.GetProperty("campaign_id").GetString());
        Assert.Equal("Pilot", root.GetProperty("name").GetString());
        Assert.Equal("Polygon", root.GetProperty("area_polygon").GetProperty("type").GetString());
        Assert.Equal(50.09, root.GetProperty("points")[0].GetProperty("lat").GetDouble(), precision: 10);
    }

    [Fact]
    public async Task SubmitRecord_posts_only_the_anonymous_wire_fields()
    {
        var (client, handler) = CreateClient();
        handler.Responses.Enqueue(FakeHandler.Json("", HttpStatusCode.Created));

        var timestamp = new DateTimeOffset(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);
        await client.SubmitRecordAsync(new AnonymousRecord(
            CampaignId.ToString("D"), "north", new string('a', 64), "blue", null, timestamp));

        var (_, body) = Assert.Single(handler.Requests);
        using var payload = JsonDocument.Parse(body!);
        var names = payload.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Exactly the PRD §4 shape — nothing that could carry a photo or plate text.
        Assert.Equal(["campaign", "color", "plate_hash", "point_id", "timestamp", "vehicle_type"], names.Order());
        Assert.Equal("north", payload.RootElement.GetProperty("point_id").GetString());
    }

    [Fact]
    public async Task GetRecords_pages_until_a_short_page_and_concatenates()
    {
        var (client, handler) = CreateClient();

        static string Row(int hour) =>
            $$"""{"campaign":"8f7c3f2a-1b2c-4d5e-9f0a-112233445566","point_id":"north","plate_hash":"{{new string('a', 64)}}","color":null,"vehicle_type":null,"timestamp":"2026-06-16T{{hour:00}}:00:00+00:00"}""";

        // First page: full (1000 rows) → client must fetch a second page.
        var fullPage = "[" + string.Join(',', Enumerable.Repeat(Row(8), 1000)) + "]";
        handler.Responses.Enqueue(FakeHandler.Json(fullPage));
        handler.Responses.Enqueue(FakeHandler.Json("[" + Row(9) + "]"));

        var records = await client.GetRecordsAsync(CampaignId);

        Assert.Equal(1001, records.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("offset=0", handler.Requests[0].Request.RequestUri!.Query);
        Assert.Contains("offset=1000", handler.Requests[1].Request.RequestUri!.Query);
        Assert.Contains("order=timestamp.asc", handler.Requests[0].Request.RequestUri!.Query);
    }

    [Fact]
    public async Task Failed_request_surfaces_status_and_body()
    {
        var (client, handler) = CreateClient();
        handler.Responses.Enqueue(FakeHandler.Json("""{"message":"new row violates row-level security"}""", HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAsync<SupabaseApiException>(() =>
            client.SubmitRecordAsync(new AnonymousRecord("c", "p", new string('a', 64), null, null, DateTimeOffset.UtcNow)));

        Assert.Contains("403", ex.Message);
        Assert.Contains("row-level security", ex.Message);
    }

    [Fact]
    public void Invalid_options_are_rejected_up_front()
    {
        Assert.Throws<ArgumentException>(() => new SupabaseClient(
            new HttpClient(), new SupabaseOptions { Url = "not-a-url", AnonKey = "k" }));
        Assert.Throws<ArgumentException>(() => new SupabaseClient(
            new HttpClient(), new SupabaseOptions { Url = "https://example.supabase.co", AnonKey = " " }));
    }
}
