namespace Prismedia.Application.Videos;

/// <summary>
/// Prepares the exact presentation timeline required to expose a stream-copy HLS source as a
/// complete VOD on its first manifest request.
/// </summary>
public interface IRemuxTimelinePreparationService {
    /// <summary>
    /// Ensures the source's exact segment timeline is durably available before a playback plan
    /// returns its remux URL.
    /// </summary>
    /// <param name="source">Resolved source selected for stream-copy playback.</param>
    /// <param name="cancellationToken">Token cancelling playback-plan preparation.</param>
    /// <returns>
    /// True when the first remux manifest can expose the complete duration; false when the caller
    /// should fall back to transcoding.
    /// </returns>
    Task<bool> PrepareAsync(VideoSourceFile source, CancellationToken cancellationToken);
}
