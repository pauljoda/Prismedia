namespace Prismedia.Application.Videos;

/// <summary>
/// Application port for resolving subtitle text assets attached to video entities.
/// </summary>
public interface IVideoSubtitleAssetService {
    /// <summary>
    /// Finds the normalized WebVTT subtitle file for one subtitle track.
    /// </summary>
    /// <param name="videoId">Video entity identifier that owns the track.</param>
    /// <param name="trackId">Subtitle track identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the lookup.</param>
    /// <returns>Subtitle asset metadata, or null when the track or file is unavailable.</returns>
    Task<VideoSubtitleAsset?> GetSubtitleAsync(
        Guid videoId,
        Guid trackId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the original ASS/SSA subtitle source for one subtitle track, when preserved.
    /// </summary>
    /// <param name="videoId">Video entity identifier that owns the track.</param>
    /// <param name="trackId">Subtitle track identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the lookup.</param>
    /// <returns>Subtitle source metadata, or null when no raw source is available.</returns>
    Task<VideoSubtitleAsset?> GetSubtitleSourceAsync(
        Guid videoId,
        Guid trackId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Subtitle file metadata needed by the API layer to serve captions.
/// </summary>
/// <param name="Path">Absolute path to the subtitle file on disk.</param>
/// <param name="ContentType">HTTP content type for the subtitle file.</param>
public sealed record VideoSubtitleAsset(string Path, string ContentType);
