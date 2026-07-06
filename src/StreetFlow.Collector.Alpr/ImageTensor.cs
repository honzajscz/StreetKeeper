using Microsoft.ML.OnnxRuntime.Tensors;
using StreetFlow.Collector.Core.Imaging;

namespace StreetFlow.Collector.Alpr;

/// <summary>Preprocessing helpers turning <see cref="RgbImage"/> into ONNX input tensors.</summary>
public static class ImageTensor
{
    /// <summary>Result of letterboxing: the tensor plus the transform needed to map boxes back.</summary>
    public sealed record Letterbox(DenseTensor<float> Tensor, float Scale, int PadX, int PadY);

    /// <summary>
    /// Resizes with preserved aspect ratio onto a gray square canvas of
    /// <paramref name="targetSize"/> (YOLO letterboxing) and emits a
    /// [1,3,H,W] tensor with 0–1 normalized values.
    /// </summary>
    public static Letterbox ToLetterboxedChw(RgbImage image, int targetSize)
    {
        var scale = Math.Min((float)targetSize / image.Width, (float)targetSize / image.Height);
        var scaledWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        var padX = (targetSize - scaledWidth) / 2;
        var padY = (targetSize - scaledHeight) / 2;

        var tensor = new DenseTensor<float>([1, 3, targetSize, targetSize]);
        const float gray = 114f / 255f;
        tensor.Fill(gray);

        for (var y = 0; y < scaledHeight; y++)
        {
            // Nearest-neighbor resample — fast, deterministic and dependency-free;
            // detector robustness dominates any resampling quality difference.
            var sourceY = Math.Min(image.Height - 1, (int)(y / scale));
            for (var x = 0; x < scaledWidth; x++)
            {
                var sourceX = Math.Min(image.Width - 1, (int)(x / scale));
                var offset = (sourceY * image.Width + sourceX) * 3;
                tensor[0, 0, padY + y, padX + x] = image.Pixels[offset] / 255f;
                tensor[0, 1, padY + y, padX + x] = image.Pixels[offset + 1] / 255f;
                tensor[0, 2, padY + y, padX + x] = image.Pixels[offset + 2] / 255f;
            }
        }

        return new Letterbox(tensor, scale, padX, padY);
    }

    /// <summary>
    /// Resizes to exactly <paramref name="width"/>×<paramref name="height"/> and emits a
    /// [1,3,H,W] tensor normalized to (x/255 − 0.5)/0.5, the PaddleOCR-rec convention.
    /// </summary>
    public static DenseTensor<float> ToNormalizedChw(RgbImage image, int width, int height)
    {
        var tensor = new DenseTensor<float>([1, 3, height, width]);
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(image.Height - 1, y * image.Height / height);
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(image.Width - 1, x * image.Width / width);
                var offset = (sourceY * image.Width + sourceX) * 3;
                tensor[0, 0, y, x] = (image.Pixels[offset] / 255f - 0.5f) / 0.5f;
                tensor[0, 1, y, x] = (image.Pixels[offset + 1] / 255f - 0.5f) / 0.5f;
                tensor[0, 2, y, x] = (image.Pixels[offset + 2] / 255f - 0.5f) / 0.5f;
            }
        }
        return tensor;
    }
}
