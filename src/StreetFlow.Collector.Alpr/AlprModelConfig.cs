using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreetFlow.Collector.Alpr;

/// <summary>
/// Describes one ALPR model candidate pair (plate detector + OCR) so the eval
/// step (spec §3.4) can swap candidates via configuration instead of code.
/// </summary>
public sealed class AlprModelConfig
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("detector")]
    public required DetectorConfig Detector { get; init; }

    [JsonPropertyName("ocr")]
    public required OcrConfig Ocr { get; init; }

    public static AlprModelConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AlprModelConfig>(json)
            ?? throw new InvalidDataException($"ALPR model config '{path}' is empty.");

        // Model paths are resolved relative to the config file so a candidate
        // directory can be copied around as one unit.
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        config.Detector.ModelPath = ResolvePath(baseDirectory, config.Detector.ModelPath);
        config.Ocr.ModelPath = ResolvePath(baseDirectory, config.Ocr.ModelPath);
        return config;
    }

    private static string ResolvePath(string baseDirectory, string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);

    public sealed class DetectorConfig
    {
        [JsonPropertyName("model_path")]
        public required string ModelPath { get; set; }

        /// <summary>Square input size of the YOLO model (e.g. 640).</summary>
        [JsonPropertyName("input_size")]
        public int InputSize { get; init; } = 640;

        [JsonPropertyName("confidence_threshold")]
        public float ConfidenceThreshold { get; init; } = 0.35f;

        [JsonPropertyName("nms_iou_threshold")]
        public float NmsIouThreshold { get; init; } = 0.45f;
    }

    public sealed class OcrConfig
    {
        [JsonPropertyName("model_path")]
        public required string ModelPath { get; set; }

        /// <summary>Input width of the recognition model (e.g. 320 for PaddleOCR rec).</summary>
        [JsonPropertyName("input_width")]
        public int InputWidth { get; init; } = 320;

        /// <summary>Input height of the recognition model (e.g. 48 for PaddleOCR v4 rec).</summary>
        [JsonPropertyName("input_height")]
        public int InputHeight { get; init; } = 48;

        /// <summary>
        /// The model's character set in class-index order, EXCLUDING the CTC blank
        /// (blank is assumed at index 0, PaddleOCR convention).
        /// </summary>
        [JsonPropertyName("charset")]
        public required string Charset { get; init; }
    }
}
