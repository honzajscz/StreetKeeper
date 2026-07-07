using System.Collections.ObjectModel;
using StreetFlow.Collector.App.Services;
using StreetFlow.Collector.Core.Captures;
using StreetFlow.Collector.Core.Plates;
using StreetFlow.Collector.Core.Sync;

namespace StreetFlow.Collector.App.Pages;

/// <summary>Row model for the verification list.</summary>
public sealed class CaptureItem
{
    public required string LocalId { get; init; }
    public required string PlateDisplay { get; init; }
    public required string Detail { get; init; }
    public required string StatusDisplay { get; init; }
    public required bool IsValid { get; init; }
    public ImageSource? PlateImage { get; init; }
}

/// <summary>
/// Verification &amp; correction UI (spec §3.9) — verification is always on.
/// Shows each capture's plate-crop photo next to the decoded plate; the operator
/// can invalidate a record, correct the plate (re-normalize + re-hash), or wipe
/// the entire local log with one button.
/// </summary>
public partial class CapturesPage : ContentPage
{
    private readonly ICaptureStore _store;
    private readonly CaptureSyncService _syncService;
    private readonly AppState _state;
    private readonly SupabaseConnection _supabase;
    private readonly ObservableCollection<CaptureItem> _items = [];

    public CapturesPage(ICaptureStore store, CaptureSyncService syncService, AppState state, SupabaseConnection supabase)
    {
        InitializeComponent();
        _store = store;
        _syncService = syncService;
        _state = state;
        _supabase = supabase;
        CapturesView.ItemsSource = _items;
        SyncButton.Text = supabase.IsConnected ? "Sync to Supabase" : "Sync (stub)";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var records = await _store.GetAllAsync();

        _items.Clear();
        foreach (var record in records.OrderByDescending(r => r.Timestamp))
        {
            var imageBytes = await _store.GetPlateImageAsync(record.LocalId);
            _items.Add(new CaptureItem
            {
                LocalId = record.LocalId,
                PlateDisplay = record.NormalizedPlate,
                Detail = $"raw OCR: {record.RawOcrText} · {record.Color ?? "?"} · " +
                         $"{record.Timestamp:HH:mm:ss} · {record.PointId} · hash {record.PlateHash[..10]}…",
                StatusDisplay = Describe(record),
                IsValid = record.Status == CaptureStatus.Valid,
                PlateImage = imageBytes is null ? null : ImageSource.FromStream(() => new MemoryStream(imageBytes)),
            });
        }

        var valid = records.Count(r => r.Status == CaptureStatus.Valid);
        SummaryLabel.Text = $"{records.Count} captures ({valid} valid). " +
                            "Photos and plate text never leave this phone.";
    }

    private static string Describe(CaptureRecord record)
    {
        var status = record.Status == CaptureStatus.Valid ? "valid" : "invalidated";
        if (record.IsCorrected)
            status += " · corrected";
        status += record.SyncState switch
        {
            SyncState.Pending => " · not sent",
            SyncState.Synced => " · sent",
            SyncState.PendingUpdate => " · needs re-send",
            _ => "",
        };
        return status;
    }

    private async void OnInvalidateClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not CaptureItem item)
            return;

        await _store.InvalidateAsync(item.LocalId);
        await RefreshAsync();
    }

    private async void OnCorrectClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not CaptureItem item)
            return;

        var corrected = await DisplayPromptAsync(
            "Correct plate",
            "Type the plate exactly as on the photo. It will be re-normalized and re-hashed.",
            initialValue: item.PlateDisplay,
            maxLength: 16);
        if (string.IsNullOrWhiteSpace(corrected))
            return;

        var campaign = _state.Campaign;
        if (campaign is null)
        {
            await DisplayAlert("Correct plate", "Load the campaign first (the hash salt is campaign-specific).", "OK");
            return;
        }

        try
        {
            await _store.CorrectPlateAsync(item.LocalId, corrected, PlateHasher.DeriveCampaignSalt(campaign.CampaignId));
        }
        catch (ArgumentException ex)
        {
            await DisplayAlert("Correct plate", ex.Message, "OK");
        }
        await RefreshAsync();
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        try
        {
            var submitted = await _syncService.SyncPendingAsync();
            var message = _supabase.IsConnected
                ? $"{submitted} anonymous record(s) sent to Supabase. Photos and plate text stayed on this phone."
                : $"{submitted} anonymous record(s) marked as sent. {_supabase.Status}";
            await DisplayAlert("Sync", message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Sync", $"Sync failed: {ex.Message}\nAlready-sent records stay marked; retry later.", "OK");
        }
        await RefreshAsync();
    }

    private async void OnDeleteAllClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert(
            "Delete all local data",
            "This wipes every capture and every plate photo stored on this phone. Continue?",
            "Delete everything", "Cancel");
        if (!confirmed)
            return;

        await _store.DeleteAllAsync();
        await RefreshAsync();
    }
}
