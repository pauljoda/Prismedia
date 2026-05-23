namespace Prismedia.Application.Videos;

/// <summary>
/// Abstraction over playback-session lifecycle, provided so endpoint tests can substitute a
/// recording stub without an entity write path. Production uses the concrete
/// <see cref="PlaybackSessionService"/>.
/// </summary>
public interface IPlaybackSessionService {
    Task StartAsync(PlaybackSessionCommand request, CancellationToken cancellationToken);
    Task ProgressAsync(PlaybackSessionCommand request, CancellationToken cancellationToken);
    Task PingAsync(PlaybackSessionCommand request, CancellationToken cancellationToken);
    Task StopAsync(PlaybackSessionCommand request, CancellationToken cancellationToken);
    Task<UserItemDataResult?> MarkPlayedAsync(Guid itemId, CancellationToken cancellationToken);
    Task<UserItemDataResult?> MarkUnplayedAsync(Guid itemId, CancellationToken cancellationToken);
}
