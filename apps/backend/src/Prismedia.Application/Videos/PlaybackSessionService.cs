using Prismedia.Application.Entities;

namespace Prismedia.Application.Videos;

/// <summary>
/// Persists Jellyfin-compatible playback events into Prismedia's shared playback capability.
/// Combines transcode session lifecycle (via <see cref="ITranscodeSessionService"/>) with
/// entity-level playback state writes, which are routed through <see cref="EntityCapabilityService"/>
/// so that Jellyfin clients and the native player produce identical playback state for the same inputs.
/// </summary>
public sealed class PlaybackSessionService : IPlaybackSessionService {
    private readonly EntityCapabilityService _capabilities;
    private readonly ITranscodeSessionService _transcodes;

    public PlaybackSessionService(EntityCapabilityService capabilities, ITranscodeSessionService transcodes) {
        _capabilities = capabilities;
        _transcodes = transcodes;
    }

    public async Task StartAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) {
        RegisterOrPing(request);

        // Jellyfin clients report the real start position in the Playing event — the saved resume
        // point when resuming — so a Playing at position 0 is an explicit "Start Over". Clear the
        // resume immediately so the item no longer offers a stale resume point even if the client
        // never reports further progress.
        if (request.ItemId != Guid.Empty && request.PositionTicks is 0) {
            await UpdatePlaybackAsync(request.ItemId, resumeSeconds: 0, completed: null, cancellationToken);
        }
    }

    public async Task ProgressAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) {
        RegisterOrPing(request);
        if (request.ItemId != Guid.Empty && request.PositionTicks is >= 0) {
            await UpdatePlaybackAsync(
                request.ItemId,
                ToSeconds(request.PositionTicks.Value),
                completed: null,
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
                request.PositionTicks is >= 0 ? ToSeconds(request.PositionTicks.Value) : 0,
                completed: null,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.PlaySessionId)) {
            await _transcodes.CancelAsync(request.PlaySessionId!, cancellationToken);
        }
    }

    public async Task<UserItemDataResult?> MarkPlayedAsync(Guid itemId, CancellationToken cancellationToken) {
        return await UpdatePlaybackAsync(itemId, resumeSeconds: 0, completed: true, cancellationToken)
            ? new UserItemDataResult(Played: true, PlaybackPositionTicks: 0)
            : null;
    }

    public async Task<UserItemDataResult?> MarkUnplayedAsync(Guid itemId, CancellationToken cancellationToken) {
        return await UpdatePlaybackAsync(itemId, resumeSeconds: 0, completed: false, cancellationToken)
            ? new UserItemDataResult(Played: false, PlaybackPositionTicks: 0)
            : null;
    }

    private async Task<bool> UpdatePlaybackAsync(
        Guid itemId,
        double resumeSeconds,
        bool? completed,
        CancellationToken cancellationToken) =>
        await _capabilities.UpdatePlaybackAsync(
            itemId,
            resumeSeconds,
            durationSeconds: null,
            completed,
            cancellationToken) is not null;

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
