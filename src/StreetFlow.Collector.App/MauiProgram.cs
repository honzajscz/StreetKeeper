using CommunityToolkit.Maui;
using StreetFlow.Collector.Alpr;
using StreetFlow.Collector.App.Pages;
using StreetFlow.Collector.App.Services;
using StreetFlow.Collector.Core.Campaign;
using StreetFlow.Collector.Core.Captures;
using StreetFlow.Collector.Core.Imaging;
using StreetFlow.Collector.Core.Sync;

namespace StreetFlow.Collector.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkitCamera();

        // Local capture store lives in the app sandbox; wiping it is a single
        // directory-level operation (privacy boundary, spec §4).
        builder.Services.AddSingleton<ICaptureStore>(_ =>
            new FileCaptureStore(Path.Combine(FileSystem.AppDataDirectory, "captures")));

        // With {AppData}/supabase.json present, campaign config and record sync go
        // against the shared database; otherwise the app keeps the iteration-1
        // offline behavior: bundled mock campaign + stub sink.
        builder.Services.AddSingleton<SupabaseConnection>();

        builder.Services.AddSingleton<ICampaignConfigSource>(sp =>
        {
            var supabase = sp.GetRequiredService<SupabaseConnection>();
            if (supabase.Client is not null)
                return new global::StreetFlow.Supabase.SupabaseCampaignConfigSource(supabase.Client);

            return new LocalJsonCampaignConfigSource(async ct =>
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("campaign.mock.json");
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(ct);
            });
        });

        builder.Services.AddSingleton<IRecordSink>(sp =>
        {
            var supabase = sp.GetRequiredService<SupabaseConnection>();
            return supabase.Client is not null
                ? new global::StreetFlow.Supabase.SupabaseRecordSink(supabase.Client)
                : new StubRecordSink();
        });
        builder.Services.AddSingleton<CaptureSyncService>();

        builder.Services.AddSingleton<IJpegEncoder, SkiaImageCodec>();
        builder.Services.AddSingleton<AlprRuntime>();
        builder.Services.AddSingleton<AppState>();

        builder.Services.AddSingleton<CampaignPage>();
        builder.Services.AddSingleton<CapturePage>();
        builder.Services.AddSingleton<CapturesPage>();

        return builder.Build();
    }
}
