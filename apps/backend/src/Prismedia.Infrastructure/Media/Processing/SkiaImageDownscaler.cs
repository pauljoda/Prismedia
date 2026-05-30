using SkiaSharp;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// In-process image downscaler backed by SkiaSharp. Decodes an existing image,
/// scales it down to a target maximum width (preserving aspect ratio, never
/// upscaling) and re-encodes it as JPEG. Used to produce small grid-card cover
/// variants without spawning a separate ffmpeg process per image.
/// </summary>
public sealed class SkiaImageDownscaler {
    /// <summary>
    /// Downscales <paramref name="sourcePath"/> to at most <paramref name="maxWidth"/>
    /// pixels wide and writes it to <paramref name="outputPath"/> as JPEG.
    /// </summary>
    /// <param name="sourcePath">Existing image file to read.</param>
    /// <param name="outputPath">Destination JPEG path; parent directory is created.</param>
    /// <param name="maxWidth">Maximum output width. The image is never upscaled.</param>
    /// <param name="quality">JPEG quality (1-100).</param>
    /// <returns>True when the output file was written successfully.</returns>
    public bool Downscale(string sourcePath, string outputPath, int maxWidth, int quality) {
        if (!File.Exists(sourcePath)) {
            return false;
        }

        using var input = File.OpenRead(sourcePath);
        using var original = SKBitmap.Decode(input);
        if (original is null || original.Width <= 0) {
            return false;
        }

        var targetWidth = Math.Min(maxWidth, original.Width);
        var targetHeight = Math.Max(1, (int)Math.Round(original.Height * (targetWidth / (double)original.Width)));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // Reuse the original when no scaling is needed so we don't allocate a copy.
        var resized = targetWidth == original.Width
            ? null
            : original.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.High);
        var source = resized ?? original;
        try {
            using var image = SKImage.FromBitmap(source);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
            if (data is null) {
                return false;
            }

            using var output = File.Create(outputPath);
            data.SaveTo(output);
            return true;
        } finally {
            resized?.Dispose();
        }
    }
}
