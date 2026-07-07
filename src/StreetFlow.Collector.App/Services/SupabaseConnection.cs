using System.Text.Json;
using StreetFlow.Supabase;

namespace StreetFlow.Collector.App.Services;

/// <summary>
/// Optional Supabase connectivity for the collector app. When
/// <c>{AppData}/supabase.json</c> exists (shape: <c>{"url": "...", "anon_key": "..."}</c>,
/// see supabase/README.md), campaign config is read from and anonymous records are
/// synced to the shared database; without it the app keeps the iteration-1
/// behavior (bundled mock campaign + stub sink), fully offline.
/// </summary>
public sealed class SupabaseConnection
{
    public SupabaseClient? Client { get; }

    public string Status { get; }

    public bool IsConnected => Client is not null;

    public SupabaseConnection()
    {
        var configPath = Path.Combine(FileSystem.AppDataDirectory, "supabase.json");
        if (!File.Exists(configPath))
        {
            Status = "Supabase not configured — using the bundled mock campaign and stub sync (nothing leaves the phone).";
            return;
        }

        try
        {
            var options = JsonSerializer.Deserialize<SupabaseOptions>(File.ReadAllText(configPath))
                ?? throw new InvalidDataException("supabase.json is empty.");
            Client = new SupabaseClient(new HttpClient(), options);
            Status = $"Supabase connected: {options.Url}";
        }
        catch (Exception ex)
        {
            Client = null;
            Status = $"supabase.json is invalid ({ex.Message}) — falling back to mock campaign and stub sync.";
        }
    }
}
