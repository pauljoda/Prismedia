namespace Prismedia.Application.Videos;

/// <summary>
/// Abstraction over playback-session lifecycle, provided so endpoint tests can substitute a
/// recording stub without an entity write path. Production uses the concrete
/// <see cref="PlaybackSessionService"/>.
/// </summary>
public interface IPlaybackSessionService {
    Task StartAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken);
    Task ProgressAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken);
    Task PingAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken);
    Task StopAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken);
}
