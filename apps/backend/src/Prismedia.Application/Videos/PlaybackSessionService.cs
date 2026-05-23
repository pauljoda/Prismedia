using Prismedia.Application.Entities;
using Prismedia.Domain.Capabilities;

namespace Prismedia.Application.Videos;

/// <summary>
/// Persists Jellyfin-compatible playback events into Prismedia's shared playback capability.
/// Combines transcode session lifecycle (via <see cref="ITranscodeSessionService"/>) with
/// entity-level playback state writes (via <see cref="IEntityWriteRepository"/>).
/// </summary>
public sealed class PlaybackSessionService : IPlaybackSessionService {
    private readonly IEntityWriteRepository _entities;
    private readonly ITranscodeSessionService _transcodes;

    public PlaybackSessionService(IEntityWriteRepository entities, ITranscodeSessionService transcodes) {
        _entities = entities;
        _transcodes = transcodes;
    }

    public Task StartAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) {
        RegisterOrPing(request);
        return Task.CompletedTask;
    }

    public async Task ProgressAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) {
        RegisterOrPing(request);
        if (request.ItemId != Guid.Empty && request.PositionTicks is >= 0) {
            await UpdatePlaybackAsync(
                request.ItemId,
                TimeSpan.FromSeconds(ToSeconds(request.PositionTicks.Value)),
                completed: false,
                cancellationToken);
        }
    }

    public Task PingAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) {
        RegisterOrPing(request);
        return Task.CompletedTask;
    }

    public async Task StopAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) {
        if (request.ItemId != Guid.Empty) {
            await UpdatePlaybackAsync(
                request.ItemId,
                request.PositionTicks is >= 0 ? TimeSpan.FromSeconds(ToSeconds(request.PositionTicks.Value)) : TimeSpan.Zero,
                completed: false,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.PlaySessionId)) {
            await _transcodes.CancelAsync(request.PlaySessionId!, cancellationToken);
        }
    }

    public async Task<UserItemDataResult?> MarkPlayedAsync(Guid itemId, CancellationToken cancellationToken) {
        return await UpdatePlaybackAsync(itemId, TimeSpan.Zero, completed: true, cancellationToken) is null
            ? null
            : new UserItemDataResult(Played: true, PlaybackPositionTicks: 0);
    }

    public async Task<UserItemDataResult?> MarkUnplayedAsync(Guid itemId, CancellationToken cancellationToken) {
        return await UpdatePlaybackAsync(itemId, TimeSpan.Zero, completed: false, cancellationToken) is null
            ? null
            : new UserItemDataResult(Played: false, PlaybackPositionTicks: 0);
    }

    private async Task<Prismedia.Domain.Entities.Entity?> UpdatePlaybackAsync(
        Guid itemId,
        TimeSpan resumeTime,
        bool completed,
        CancellationToken cancellationToken) {
        var entity = await _entities.FindAsync(itemId, cancellationToken);
        if (entity is null) {
            return null;
        }

        var playback = entity.GetCapability<CapabilityPlayback>();
        if (playback is null) {
            playback = new CapabilityPlayback();
            entity.AddCapability(playback);
        }

        playback.MarkPlayed(resumeTime, DateTimeOffset.UtcNow);
        await _entities.SaveAsync(entity, cancellationToken);
        return entity;
    }

    private void RegisterOrPing(PlaybackSessionCommand request) {
        if (string.IsNullOrWhiteSpace(request.PlaySessionId)) {
            return;
        }

        if (request.ItemId == Guid.Empty) {
            _transcodes.Ping(request.PlaySessionId!);
            return;
        }

        _transcodes.Register(request.PlaySessionId!, request.ItemId);
        _transcodes.Ping(request.PlaySessionId!);
    }

    private static double ToSeconds(long ticks) =>
        Math.Max(0, ticks / (double)TimeSpan.TicksPerSecond);
}
