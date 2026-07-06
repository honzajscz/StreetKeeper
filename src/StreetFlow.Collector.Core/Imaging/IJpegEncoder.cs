namespace StreetFlow.Collector.Core.Imaging;

/// <summary>
/// Encodes an in-memory RGB image to JPEG for the local capture store.
/// Platform-specific (SkiaSharp in the ML/app layer); abstracted so the core
/// pipeline stays dependency-free and testable.
/// </summary>
public interface IJpegEncoder
{
    byte[] EncodeJpeg(RgbImage image, int quality = 85);
}
