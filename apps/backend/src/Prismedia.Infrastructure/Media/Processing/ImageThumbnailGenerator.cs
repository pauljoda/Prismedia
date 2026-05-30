using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Core image thumbnail resizer. Tries SkiaSharp in-process first (no process spawn,
/// covers jpg/png/webp/gif/bmp — the overwhelming majority of files) and falls back to
/// ffmpeg only for inputs Skia cannot decode (heic/avif/svg, or gallery "image" entries
/// that are actually short video clips). The fallback is automatic: Skia returns false
/// when it cannot read the source, so callers never branch on format.
/// </summary>
public sealed class ImageThumbnailGenerator(
    SkiaImageDownscaler skia,
    ThumbnailService ffmpeg) : IImageThumbnailGenerator {
    /// <inheritdoc />
    public async Task<bool> GenerateAsync(
        string sourcePath, string outputPath, int maxWidth, int jpegQuality, CancellationToken cancellationToken) {
        if (skia.Downscale(sourcePath, outputPath, maxWidth, jpegQuality)) {
            return true;
        }

        // ffmpeg uses an inverse -q:v scale (lower is better); map the JPEG quality onto its useful range.
        var ffmpegQuality = Math.Clamp((100 - jpegQuality) / 10 + 1, 2, 10);
        return await ffmpeg.GenerateImageThumbnailAsync(sourcePath, outputPath, maxWidth, ffmpegQuality, cancellationToken);
    }
}
