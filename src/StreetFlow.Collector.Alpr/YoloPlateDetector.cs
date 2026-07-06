using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using StreetFlow.Collector.Core.Alpr;
using StreetFlow.Collector.Core.Imaging;

namespace StreetFlow.Collector.Alpr;

/// <summary>
/// YOLO-based ONNX license-plate detector (spec §3.4, phase 1). Handles both
/// common single-class export layouts:
/// YOLOv5 — [1, N, 5+nc] (cx, cy, w, h, objectness, class scores…) and
/// YOLOv8/11 — [1, 4+nc, N] (cx, cy, w, h, class scores…).
/// The concrete model file is a candidate chosen by the eval step.
/// </summary>
public sealed class YoloPlateDetector : IPlateDetector, IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly AlprModelConfig.DetectorConfig _config;

    public YoloPlateDetector(AlprModelConfig.DetectorConfig config, SessionOptions? sessionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _session = sessionOptions is null
            ? new InferenceSession(config.ModelPath)
            : new InferenceSession(config.ModelPath, sessionOptions);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public IReadOnlyList<PlateDetection> Detect(RgbImage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var letterbox = ImageTensor.ToLetterboxedChw(frame, _config.InputSize);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, letterbox.Tensor)]);
        var output = results[0].AsTensor<float>();

        var raw = DecodeCandidates(output);
        var kept = NonMaxSuppression(raw, _config.NmsIouThreshold);

        var detections = new List<PlateDetection>(kept.Count);
        foreach (var candidate in kept)
        {
            // Map from letterbox coordinates back to the original frame.
            var x = (candidate.Cx - candidate.W / 2 - letterbox.PadX) / letterbox.Scale;
            var y = (candidate.Cy - candidate.H / 2 - letterbox.PadY) / letterbox.Scale;
            var box = new RectI(
                (int)Math.Round(x),
                (int)Math.Round(y),
                (int)Math.Round(candidate.W / letterbox.Scale),
                (int)Math.Round(candidate.H / letterbox.Scale))
                .ClampTo(frame.Width, frame.Height);

            if (!box.IsEmpty)
                detections.Add(new PlateDetection(box, candidate.Score));
        }

        return detections;
    }

    private readonly record struct Candidate(float Cx, float Cy, float W, float H, float Score);

    private List<Candidate> DecodeCandidates(Tensor<float> output)
    {
        if (output.Dimensions.Length != 3)
            throw new InvalidDataException($"Unexpected detector output rank {output.Dimensions.Length}; expected 3.");

        var dim1 = output.Dimensions[1];
        var dim2 = output.Dimensions[2];
        // v5 layout has boxes on dim1 ([1, 25200, 5+nc]); v8 has attributes on
        // dim1 ([1, 4+nc, 8400]). Attribute count is always the smaller dimension.
        var attributesFirst = dim1 < dim2;
        var attributeCount = attributesFirst ? dim1 : dim2;
        var boxCount = attributesFirst ? dim2 : dim1;
        var hasObjectness = attributeCount >= 5 && !attributesFirst; // v5-style exports carry objectness

        var candidates = new List<Candidate>();
        for (var i = 0; i < boxCount; i++)
        {
            float At(int attribute) => attributesFirst ? output[0, attribute, i] : output[0, i, attribute];

            float score;
            if (hasObjectness)
            {
                var objectness = At(4);
                var best = 1f;
                if (attributeCount > 5)
                {
                    best = 0f;
                    for (var c = 5; c < attributeCount; c++)
                        best = Math.Max(best, At(c));
                }
                score = objectness * best;
            }
            else
            {
                score = 0f;
                for (var c = 4; c < attributeCount; c++)
                    score = Math.Max(score, At(c));
            }

            if (score < _config.ConfidenceThreshold)
                continue;

            candidates.Add(new Candidate(At(0), At(1), At(2), At(3), score));
        }

        return candidates;
    }

    private static List<Candidate> NonMaxSuppression(List<Candidate> candidates, float iouThreshold)
    {
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        var kept = new List<Candidate>();

        foreach (var candidate in candidates)
        {
            var suppressed = false;
            foreach (var keeper in kept)
            {
                if (IntersectionOverUnion(candidate, keeper) > iouThreshold)
                {
                    suppressed = true;
                    break;
                }
            }
            if (!suppressed)
                kept.Add(candidate);
        }

        return kept;
    }

    private static float IntersectionOverUnion(Candidate a, Candidate b)
    {
        var ax1 = a.Cx - a.W / 2; var ay1 = a.Cy - a.H / 2; var ax2 = a.Cx + a.W / 2; var ay2 = a.Cy + a.H / 2;
        var bx1 = b.Cx - b.W / 2; var by1 = b.Cy - b.H / 2; var bx2 = b.Cx + b.W / 2; var by2 = b.Cy + b.H / 2;

        var interWidth = Math.Max(0, Math.Min(ax2, bx2) - Math.Max(ax1, bx1));
        var interHeight = Math.Max(0, Math.Min(ay2, by2) - Math.Max(ay1, by1));
        var intersection = interWidth * interHeight;
        var union = a.W * a.H + b.W * b.H - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    public void Dispose() => _session.Dispose();
}
