namespace Prismedia.Application.Videos;

/// <summary>
/// Abstraction over video playback planning, provided so endpoint tests can substitute a
/// stub without spinning up the full source + transcode pipeline. Production uses the
/// concrete <see cref="VideoPlaybackPlanService"/>.
/// </summary>
public interface IVideoPlaybackPlanService {
    /// <summary>
    /// Builds a playback response for one media item and client request.
    /// </summary>
    Task<VideoPlaybackPlanResult?> CreatePlanAsync(
        Guid entityId,
        VideoPlaybackPlanQuery? request,
        CancellationToken cancellationToken);
}
