using StreetFlow.Collector.Core.Imaging;

namespace StreetFlow.Collector.Core.Alpr;

/// <summary>A raw OCR reading of a plate crop, before normalization.</summary>
public sealed record OcrResult(string Text, float Confidence);

/// <summary>
/// Phase 2 of the ALPR pipeline: read the characters of a cropped plate.
/// Implemented by a CRNN / PaddleOCR-rec ONNX model in the ML layer; the concrete
/// model is chosen by the eval step (spec §3.4).
/// </summary>
public interface IPlateOcr
{
    OcrResult Recognize(RgbImage plateCrop);
}
