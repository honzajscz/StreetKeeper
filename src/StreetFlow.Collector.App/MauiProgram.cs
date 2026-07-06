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

        // Iteration 1: campaign config comes from the bundled mock JSON; the
        // Supabase-backed source replaces this registration in a later surface.
        builder.Services.AddSingleton<ICampaignConfigSource>(_ =>
            new LocalJsonCampaignConfigSource(async ct =>
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("campaign.mock.json");
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(ct);
            }));

        // Iteration 1: sync is a stub — records only get marked, nothing leaves the phone (§3.10).
        builder.Services.AddSingleton<IRecordSink, StubRecordSink>();
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
