using StreetFlow.Collector.App.Services;
using StreetFlow.Collector.Core.Campaign;
using StreetFlow.Collector.Core.Geo;

namespace StreetFlow.Collector.App.Pages;

public partial class CampaignPage : ContentPage
{
    /// <summary>Campaign id of the bundled mock config (Resources/Raw/campaign.mock.json).</summary>
    private const string DemoCampaignId = "8f7c3f2a-1b2c-4d5e-9f0a-112233445566";

    private readonly ICampaignConfigSource _configSource;
    private readonly AppState _state;
    private readonly AlprRuntime _alpr;

    public CampaignPage(ICampaignConfigSource configSource, AppState state, AlprRuntime alpr, SupabaseConnection supabase)
    {
        InitializeComponent();
        _configSource = configSource;
        _state = state;
        _alpr = alpr;
        CampaignIdEntry.Text = DemoCampaignId;
        DataSourceHint.Text = $"Enter the campaign ID handed out by the organizer. {supabase.Status}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _alpr.TryLoad();
        AlprStatusLabel.Text = _alpr.Status;
    }

    private async void OnLoadCampaignClicked(object? sender, EventArgs e)
    {
        if (!Guid.TryParse(CampaignIdEntry.Text?.Trim(), out var campaignId))
        {
            await DisplayAlert("Campaign", "That is not a valid campaign ID (GUID).", "OK");
            return;
        }

        LoadCampaignButton.IsEnabled = false;
        try
        {
            var campaign = await _configSource.GetConfigAsync(campaignId);
            if (campaign is null)
            {
                await DisplayAlert("Campaign", "Unknown campaign ID.", "OK");
                return;
            }

            _state.SetCampaign(campaign);
            CampaignInfoLabel.Text =
                $"{campaign.Name}\n{campaign.Points.Count} boundary points: " +
                string.Join(", ", campaign.Points.Select(p => p.Name));
            CampaignInfoLabel.IsVisible = true;
            DetectPointButton.IsEnabled = true;
            PointInfoLabel.IsVisible = false;
            StartButton.IsEnabled = false;
        }
        catch (CampaignConfigFormatException ex)
        {
            await DisplayAlert("Campaign", $"Campaign config is invalid: {ex.Message}", "OK");
        }
        finally
        {
            LoadCampaignButton.IsEnabled = true;
        }
    }

    private async void OnDetectPointClicked(object? sender, EventArgs e)
    {
        var permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (permission != PermissionStatus.Granted)
        {
            await DisplayAlert("GPS", "Location permission is required to detect your boundary point.", "OK");
            return;
        }

        DetectPointButton.IsEnabled = false;
        try
        {
            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(15)));
            if (location is null)
            {
                await DisplayAlert("GPS", "Could not get a GPS fix. Try again outdoors.", "OK");
                return;
            }

            var result = _state.DetectPoint(new GeoLocation(location.Latitude, location.Longitude));
            if (result is null)
            {
                PointInfoLabel.Text = "No campaign point within range. Are you standing at your assigned point?";
                PointInfoLabel.IsVisible = true;
                StartButton.IsEnabled = false;
                return;
            }

            PointInfoLabel.Text = $"You are at: {result.Point.Name} ({result.Point.PointId}), {result.DistanceMeters:F0} m away.";
            PointInfoLabel.IsVisible = true;
            StartButton.IsEnabled = true;
        }
        finally
        {
            DetectPointButton.IsEnabled = true;
        }
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        if (!_state.TryStartCaptureSession(out var error))
        {
            await DisplayAlert("Capture", error, "OK");
            return;
        }

        await Shell.Current.GoToAsync("//capture");
    }
}
