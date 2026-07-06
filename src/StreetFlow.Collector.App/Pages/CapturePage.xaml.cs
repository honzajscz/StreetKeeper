using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using StreetFlow.Collector.Alpr;
using StreetFlow.Collector.App.Services;

namespace StreetFlow.Collector.App.Pages;

public partial class CapturePage : ContentPage
{
    /// <summary>
    /// Frame sampling interval. Tuning this against battery/performance is an
    /// open question in the spec (§8); ~1.4 s keeps a mid-range phone responsive.
    /// </summary>
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(1400);

    private readonly AppState _state;
    private IDispatcherTimer? _timer;
    private bool _collecting;
    private bool _frameInFlight;
    private int _sessionCaptureCount;

    public CapturePage(AppState state)
    {
        InitializeComponent();
        _state = state;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_state.CaptureSession is null)
        {
            SessionLabel.Text = "No capture session — load a campaign and detect your point first.";
            ToggleButton.IsEnabled = false;
            return;
        }

        SessionLabel.Text = $"Campaign: {_state.Campaign!.Name} · point: {_state.DetectedPoint!.Point.Name}";

        var permission = await Permissions.RequestAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
        {
            SessionLabel.Text = "Camera permission is required to collect.";
            ToggleButton.IsEnabled = false;
            return;
        }

        ToggleButton.IsEnabled = true;
        await Camera.StartCameraPreview(CancellationToken.None);
    }

    protected override void OnDisappearing()
    {
        StopSampling();
        Camera.StopCameraPreview();
        base.OnDisappearing();
    }

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        if (_collecting)
        {
            StopSampling();
        }
        else
        {
            _collecting = true;
            ToggleButton.Text = "Stop collection";
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = SampleInterval;
            _timer.Tick += OnSampleTick;
            _timer.Start();
        }
    }

    private void StopSampling()
    {
        _collecting = false;
        ToggleButton.Text = "Start collection";
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnSampleTick;
            _timer = null;
        }
    }

    private async void OnSampleTick(object? sender, EventArgs e)
    {
        if (_frameInFlight || !_collecting)
            return;

        _frameInFlight = true;
        try
        {
            await Camera.CaptureImage(CancellationToken.None);
            // Processing continues in OnMediaCaptured; _frameInFlight is released there.
        }
        catch
        {
            _frameInFlight = false;
        }
    }

    private async void OnMediaCaptured(object? sender, MediaCapturedEventArgs e)
    {
        try
        {
            var session = _state.CaptureSession;
            if (session is null || !_collecting)
                return;

            using var memory = new MemoryStream();
            await e.Media.CopyToAsync(memory);

            // The full frame exists only in memory for the duration of this call;
            // only the plate crop is persisted, and only locally (spec §4).
            var frame = SkiaImageCodec.Decode(memory.ToArray());
            var stored = await session.ProcessFrameAsync(frame, DateTimeOffset.Now);

            if (stored.Count > 0)
            {
                _sessionCaptureCount += stored.Count;
                await MainThread.InvokeOnMainThreadAsync(() =>
                    StatsLabel.Text = $"Captured this session: {_sessionCaptureCount} · last: {stored[^1].NormalizedPlate}");
            }
        }
        catch
        {
            // A failed frame is not fatal during collection; the next sample retries.
        }
        finally
        {
            _frameInFlight = false;
        }
    }
}
