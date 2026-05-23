namespace Prismedia.Application.Videos;

/// <summary>
/// Abstraction over playback-info negotiation, provided so endpoint tests can substitute a
/// stub without spinning up the full source + transcode pipeline. Production uses the
/// concrete <see cref="PlaybackInfoService"/>.
/// </summary>
public interface IPlaybackInfoService {
    /// <summary>
    /// Builds a playback response for one media item and client request.
    /// </summary>
    Task<PlaybackInfoResult?> GetPlaybackInfoAsync(
        Guid itemId,
        PlaybackInfoQuery? request,
        CancellationToken cancellationToken);
}
