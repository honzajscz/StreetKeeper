using StreetFlow.Collector.Core.Imaging;

namespace StreetFlow.Collector.Core.Alpr;

/// <summary>A candidate license-plate region found in a frame.</summary>
public readonly record struct PlateDetection(RectI Box, float Confidence);

/// <summary>
/// Phase 1 of the ALPR pipeline: locate plate candidates in a camera frame.
/// Implemented by a YOLO-based ONNX detector in the ML layer; the concrete model
/// is chosen by the eval step (spec §3.4).
/// </summary>
public interface IPlateDetector
{
    IReadOnlyList<PlateDetection> Detect(RgbImage frame);
}
