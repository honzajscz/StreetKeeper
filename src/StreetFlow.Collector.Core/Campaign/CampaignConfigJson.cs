using System.Text.Json;

namespace StreetFlow.Collector.Core.Campaign;

/// <summary>
/// Parses campaign configuration JSON in the wire shape defined by the PRD:
/// <code>
/// {
///   "campaign_id": "&lt;GUID&gt;",
///   "name": "&lt;name&gt;",
///   "area_polygon": &lt;GeoJSON polygon (object or string)&gt;,
///   "points": [ { "point_id": "...", "name": "...", "lat": 0.0, "lng": 0.0 } ]
/// }
/// </code>
/// </summary>
public static class CampaignConfigJson
{
    /// <summary>Parses a single campaign configuration object.</summary>
    /// <exception cref="CampaignConfigFormatException">The JSON is malformed or misses required fields.</exception>
    public static CampaignConfig Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CampaignConfigFormatException("Campaign config is not valid JSON.", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new CampaignConfigFormatException("Campaign config must be a JSON object.");
            return ParseElement(document.RootElement);
        }
    }

    /// <summary>
    /// Parses a document containing either a single campaign object or an array of campaigns
    /// (the mock file format used in iteration 1 may bundle several campaigns).
    /// </summary>
    public static IReadOnlyList<CampaignConfig> ParseMany(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CampaignConfigFormatException("Campaign config is not valid JSON.", ex);
        }

        using (document)
        {
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => [ParseElement(document.RootElement)],
                JsonValueKind.Array => document.RootElement.EnumerateArray().Select(ParseElement).ToList(),
                _ => throw new CampaignConfigFormatException("Campaign config must be a JSON object or an array of objects."),
            };
        }
    }

    private static CampaignConfig ParseElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new CampaignConfigFormatException("Each campaign config must be a JSON object.");

        if (!element.TryGetProperty("campaign_id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
            throw new CampaignConfigFormatException("Campaign config is missing the required string field 'campaign_id'.");
        if (!Guid.TryParse(idElement.GetString(), out var campaignId))
            throw new CampaignConfigFormatException($"'campaign_id' is not a valid GUID: '{idElement.GetString()}'.");

        var name = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()!
            : throw new CampaignConfigFormatException("Campaign config is missing the required string field 'name'.");

        // The polygon is authored by the Organizer Console; the collector never interprets
        // it, so it is preserved verbatim whether it arrives as a GeoJSON object or a string.
        string? areaPolygon = null;
        if (element.TryGetProperty("area_polygon", out var polygonElement))
        {
            areaPolygon = polygonElement.ValueKind switch
            {
                JsonValueKind.String => polygonElement.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => polygonElement.GetRawText(),
            };
        }

        var points = new List<CampaignPoint>();
        if (element.TryGetProperty("points", out var pointsElement))
        {
            if (pointsElement.ValueKind != JsonValueKind.Array)
                throw new CampaignConfigFormatException("'points' must be an array.");

            foreach (var pointElement in pointsElement.EnumerateArray())
                points.Add(ParsePoint(pointElement));
        }

        return new CampaignConfig(campaignId, name, areaPolygon, points);
    }

    private static CampaignPoint ParsePoint(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new CampaignConfigFormatException("Each entry in 'points' must be a JSON object.");

        if (!element.TryGetProperty("point_id", out var idElement) || idElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idElement.GetString()))
            throw new CampaignConfigFormatException("A campaign point is missing the required string field 'point_id'.");

        var name = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()!
            : string.Empty;

        if (!element.TryGetProperty("lat", out var latElement) || latElement.ValueKind != JsonValueKind.Number || !latElement.TryGetDouble(out var lat))
            throw new CampaignConfigFormatException($"Campaign point '{idElement.GetString()}' is missing the required numeric field 'lat'.");
        if (!element.TryGetProperty("lng", out var lngElement) || lngElement.ValueKind != JsonValueKind.Number || !lngElement.TryGetDouble(out var lng))
            throw new CampaignConfigFormatException($"Campaign point '{idElement.GetString()}' is missing the required numeric field 'lng'.");

        return new CampaignPoint(idElement.GetString()!, name, lat, lng);
    }
}

/// <summary>Thrown when campaign configuration JSON cannot be parsed.</summary>
public sealed class CampaignConfigFormatException : Exception
{
    public CampaignConfigFormatException(string message) : base(message) { }
    public CampaignConfigFormatException(string message, Exception inner) : base(message, inner) { }
}
