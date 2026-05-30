namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Core image-to-image thumbnail resizer shared by every entity kind. Downscales an
/// existing image file to a target width as JPEG. Implementations resize in-process
/// where possible and fall back to a heavier path only for formats the fast path
/// cannot decode, so callers never branch on image format.
/// </summary>
public interface IImageThumbnailGenerator {
    /// <summary>
    /// Downscales <paramref name="sourcePath"/> to at most <paramref name="maxWidth"/>
    /// pixels wide (aspect preserved, never upscaled) and writes JPEG to
    /// <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="sourcePath">Existing image file to read.</param>
    /// <param name="outputPath">Destination JPEG path; parent directory is created.</param>
    /// <param name="maxWidth">Maximum output width.</param>
    /// <param name="jpegQuality">JPEG quality, 1-100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the thumbnail was written successfully.</returns>
    Task<bool> GenerateAsync(
        string sourcePath, string outputPath, int maxWidth, int jpegQuality, CancellationToken cancellationToken);
}
