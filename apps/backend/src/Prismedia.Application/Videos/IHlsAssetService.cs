namespace Prismedia.Application.Videos;

/// <summary>
/// Application port for locating generated HLS manifests, playlists, and media segments.
/// </summary>
public interface IHlsAssetService {
    /// <summary>
    /// Finds a generated HLS asset within the cache package for one video.
    /// </summary>
    /// <param name="id">Video entity identifier.</param>
    /// <param name="assetPath">Package-relative HLS asset path.</param>
    /// <param name="audioStreamIndex">Optional source audio stream index to use for virtual HLS.</param>
    /// <param name="cancellationToken">Token used to cancel the lookup.</param>
    /// <returns>HLS asset metadata, or null when the requested asset is missing or invalid.</returns>
    Task<HlsAsset?> GetAssetAsync(
        Guid id,
        string assetPath,
        int? audioStreamIndex,
        CancellationToken cancellationToken);
}

/// <summary>
/// HLS asset metadata needed by the API layer to serve cached adaptive playback files.
/// </summary>
/// <param name="Path">Absolute path to the cached asset on disk.</param>
/// <param name="ContentType">HTTP content type for the asset.</param>
/// <param name="CacheControl">Cache-control value appropriate for the asset kind.</param>
public sealed record HlsAsset(
    string Path,
    string ContentType,
    string CacheControl);
