using StreetFlow.Collector.Core.Campaign;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class CampaignConfigJsonTests
{
    private const string PrdShapedJson = """
        {
          "campaign_id": "8f7c3f2a-1b2c-4d5e-9f0a-112233445566",
          "name": "Vinohradská pilot",
          "area_polygon": {
            "type": "Polygon",
            "coordinates": [[[14.41, 50.08], [14.43, 50.08], [14.43, 50.09], [14.41, 50.09], [14.41, 50.08]]]
          },
          "points": [
            { "point_id": "north", "name": "sever — vjezd", "lat": 50.0910, "lng": 14.4200 },
            { "point_id": "south", "name": "jih — výjezd", "lat": 50.0880, "lng": 14.4210 }
          ]
        }
        """;

    [Fact]
    public void Parses_the_prd_wire_shape()
    {
        var config = CampaignConfigJson.Parse(PrdShapedJson);

        Assert.Equal(Guid.Parse("8f7c3f2a-1b2c-4d5e-9f0a-112233445566"), config.CampaignId);
        Assert.Equal("Vinohradská pilot", config.Name);
        Assert.NotNull(config.AreaPolygonGeoJson);
        Assert.Contains("Polygon", config.AreaPolygonGeoJson);
        Assert.Equal(2, config.Points.Count);
        Assert.Equal(new CampaignPoint("north", "sever — vjezd", 50.0910, 14.4200), config.Points[0]);
        Assert.Equal(new CampaignPoint("south", "jih — výjezd", 50.0880, 14.4210), config.Points[1]);
    }

    [Fact]
    public void Area_polygon_may_be_a_string_or_missing()
    {
        var withString = CampaignConfigJson.Parse("""
            { "campaign_id": "8f7c3f2a-1b2c-4d5e-9f0a-112233445566", "name": "X", "area_polygon": "{\"type\":\"Polygon\"}", "points": [] }
            """);
        Assert.Equal("{\"type\":\"Polygon\"}", withString.AreaPolygonGeoJson);

        var without = CampaignConfigJson.Parse("""
            { "campaign_id": "8f7c3f2a-1b2c-4d5e-9f0a-112233445566", "name": "X", "points": [] }
            """);
        Assert.Null(without.AreaPolygonGeoJson);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "name": "missing id", "points": [] }""")]
    [InlineData("""{ "campaign_id": "not-a-guid", "name": "X", "points": [] }""")]
    [InlineData("""{ "campaign_id": "8f7c3f2a-1b2c-4d5e-9f0a-112233445566", "points": [] }""")]
    [InlineData("""{ "campaign_id": "8f7c3f2a-1b2c-4d5e-9f0a-112233445566", "name": "X", "points": [ { "name": "no id", "lat": 1, "lng": 2 } ] }""")]
    [InlineData("""{ "campaign_id": "8f7c3f2a-1b2c-4d5e-9f0a-112233445566", "name": "X", "points": [ { "point_id": "p", "name": "no coords" } ] }""")]
    public void Malformed_config_throws_a_descriptive_format_exception(string json)
    {
        Assert.Throws<CampaignConfigFormatException>(() => CampaignConfigJson.Parse(json));
    }

    [Fact]
    public void ParseMany_accepts_single_object_and_array()
    {
        Assert.Single(CampaignConfigJson.ParseMany(PrdShapedJson));

        var two = CampaignConfigJson.ParseMany($"[{PrdShapedJson}, {PrdShapedJson.Replace("8f7c3f2a", "aa7c3f2a")}]");
        Assert.Equal(2, two.Count);
    }
}

public class LocalJsonCampaignConfigSourceTests
{
    private static readonly Guid KnownId = Guid.Parse("8f7c3f2a-1b2c-4d5e-9f0a-112233445566");

    private const string Json = """
        { "campaign_id": "8f7c3f2a-1b2c-4d5e-9f0a-112233445566", "name": "Pilot", "points": [] }
        """;

    [Fact]
    public async Task Returns_campaign_matching_the_requested_id()
    {
        var source = LocalJsonCampaignConfigSource.FromJson(Json);
        var config = await source.GetConfigAsync(KnownId);

        Assert.NotNull(config);
        Assert.Equal("Pilot", config.Name);
    }

    [Fact]
    public async Task Returns_null_for_unknown_campaign()
    {
        var source = LocalJsonCampaignConfigSource.FromJson(Json);
        Assert.Null(await source.GetConfigAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Reads_from_a_file_on_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"streetflow-config-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, Json);
        try
        {
            var source = LocalJsonCampaignConfigSource.FromFile(path);
            var config = await source.GetConfigAsync(KnownId);
            Assert.NotNull(config);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
