using StreetFlow.Collector.Alpr;
using StreetFlow.Collector.Core.Alpr;

namespace StreetFlow.Collector.App.Services;

/// <summary>
/// Loads the ALPR model pair chosen by the eval step (spec §3.4) from
/// <c>{AppData}/models/alpr.json</c>. The model files deliberately are not
/// bundled with the app in iteration 1 — the eval step decides which candidate
/// ships. When no models are present the app still runs (camera, GPS, store,
/// verification UI) and reports why capture is inactive.
/// </summary>
public sealed class AlprRuntime : IDisposable
{
    private YoloPlateDetector? _detector;
    private CtcPlateOcr? _ocr;

    public AlprPipeline? Pipeline { get; private set; }

    public bool IsReady => Pipeline is not null;

    public string Status { get; private set; } = "ALPR models not loaded yet.";

    public string ModelsDirectory => Path.Combine(FileSystem.AppDataDirectory, "models");

    /// <summary>Attempts to (re)load the model pair; safe to call repeatedly.</summary>
    public bool TryLoad()
    {
        if (IsReady)
            return true;

        var configPath = Path.Combine(ModelsDirectory, "alpr.json");
        if (!File.Exists(configPath))
        {
            Status = $"No ALPR models installed. Copy the eval-step winner (alpr.json + .onnx files) to: {ModelsDirectory}";
            return false;
        }

        try
        {
            var config = AlprModelConfig.Load(configPath);
            _detector = new YoloPlateDetector(config.Detector);
            _ocr = new CtcPlateOcr(config.Ocr);
            Pipeline = new AlprPipeline(_detector, _ocr);
            Status = $"ALPR ready: {config.Name}";
            return true;
        }
        catch (Exception ex)
        {
            Dispose();
            Status = $"Failed to load ALPR models: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        Pipeline = null;
        _detector?.Dispose();
        _detector = null;
        _ocr?.Dispose();
        _ocr = null;
    }
}
