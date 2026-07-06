using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using StreetFlow.Collector.Core.Alpr;
using StreetFlow.Collector.Core.Imaging;

namespace StreetFlow.Collector.Alpr;

/// <summary>
/// CTC-decoded ONNX plate OCR (spec §3.4, phase 2) — fits CRNN and
/// PaddleOCR-rec style models: input [1,3,H,W], output [1, T, C] (or [1, C, T])
/// with the CTC blank at class index 0. Greedy decode; confidence is the mean
/// probability of the emitted characters.
/// </summary>
public sealed class CtcPlateOcr : IPlateOcr, IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly AlprModelConfig.OcrConfig _config;

    public CtcPlateOcr(AlprModelConfig.OcrConfig config, SessionOptions? sessionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrEmpty(config.Charset))
            throw new ArgumentException("OCR charset must not be empty.", nameof(config));
        _config = config;
        _session = sessionOptions is null
            ? new InferenceSession(config.ModelPath)
            : new InferenceSession(config.ModelPath, sessionOptions);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public OcrResult Recognize(RgbImage plateCrop)
    {
        ArgumentNullException.ThrowIfNull(plateCrop);

        var tensor = ImageTensor.ToNormalizedChw(plateCrop, _config.InputWidth, _config.InputHeight);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        var output = results[0].AsTensor<float>();

        if (output.Dimensions.Length != 3)
            throw new InvalidDataException($"Unexpected OCR output rank {output.Dimensions.Length}; expected 3.");

        // Class count = charset + blank. Use it to tell [1,T,C] from [1,C,T].
        var classCount = _config.Charset.Length + 1;
        var timeMajor = output.Dimensions[2] == classCount ||
                        (output.Dimensions[1] != classCount && output.Dimensions[1] >= output.Dimensions[2]);
        var timeSteps = timeMajor ? output.Dimensions[1] : output.Dimensions[2];
        var classes = timeMajor ? output.Dimensions[2] : output.Dimensions[1];

        var text = new System.Text.StringBuilder();
        var probabilities = new List<float>();
        var previousClass = 0;

        for (var t = 0; t < timeSteps; t++)
        {
            var bestClass = 0;
            var bestValue = float.MinValue;
            for (var c = 0; c < classes; c++)
            {
                var value = timeMajor ? output[0, t, c] : output[0, c, t];
                if (value > bestValue)
                {
                    bestValue = value;
                    bestClass = c;
                }
            }

            // CTC collapse: skip blanks (index 0) and repeats of the previous emission.
            if (bestClass != 0 && bestClass != previousClass)
            {
                var charIndex = bestClass - 1;
                if (charIndex < _config.Charset.Length)
                {
                    text.Append(_config.Charset[charIndex]);
                    probabilities.Add(NormalizeProbability(bestValue, output, t, classes, timeMajor));
                }
            }
            previousClass = bestClass;
        }

        var confidence = probabilities.Count == 0 ? 0f : probabilities.Average();
        return new OcrResult(text.ToString(), confidence);
    }

    /// <summary>
    /// PaddleOCR rec exports emit softmaxed probabilities; raw CRNN exports may emit
    /// logits. Softmax the step only when values are clearly not probabilities.
    /// </summary>
    private static float NormalizeProbability(float bestValue, Tensor<float> output, int t, int classes, bool timeMajor)
    {
        if (bestValue is >= 0f and <= 1f)
            return bestValue;

        double sum = 0;
        for (var c = 0; c < classes; c++)
        {
            var value = timeMajor ? output[0, t, c] : output[0, c, t];
            sum += Math.Exp(value - bestValue);
        }
        return (float)(1.0 / sum);
    }

    public void Dispose() => _session.Dispose();
}
