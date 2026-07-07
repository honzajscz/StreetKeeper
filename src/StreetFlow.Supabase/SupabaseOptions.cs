using System.Text.Json.Serialization;

namespace StreetFlow.Supabase;

/// <summary>Connection settings of the shared Supabase project (URL + anon key).</summary>
public sealed class SupabaseOptions
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("anon_key")]
    public required string AnonKey { get; init; }

    public void Validate()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new ArgumentException($"Supabase URL is not a valid http(s) URL: '{Url}'.");
        if (string.IsNullOrWhiteSpace(AnonKey))
            throw new ArgumentException("Supabase anon key must not be empty.");
    }
}
