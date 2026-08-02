using Prismedia.Application.Entities;

namespace Prismedia.Application.Videos;

/// <summary>
/// Persists native playback-session progress into Prismedia's shared consumption capability.
/// Combines transcode session lifecycle (via <see cref="ITranscodeSessionService"/>) with
/// entity-level playback state writes routed through <see cref="EntityCapabilityService"/>.
/// </summary>
public sealed class PlaybackSessionService : IPlaybackSessionService {
    private readonly EntityCapabilityService _capabilities;
    private readonly ITranscodeSessionService _transcodes;
    private readonly TimeProvider _timeProvider;

    public PlaybackSessionService(
        EntityCapabilityService capabilities,
        ITranscodeSessionService transcodes,
        TimeProvider? timeProvider = null) {
        _capabilities = capabilities;
        _transcodes = transcodes;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task StartAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken) {
        RegisterOrPing(request);

        if (request.EntityId != Guid.Empty) {
            await _capabilities.RecordAccessedAsync(
                request.EntityId,
                _timeProvider.GetUtcNow(),
                request.PositionSeconds,
                request.DurationSeconds,
                request.SessionId,
                cancellationToken);
        }

        // Starting at zero is an explicit "Start Over" signal. Clear a stale resume point even if
        // the client never sends another progress event.
        if (request.EntityId != Guid.Empty && request.PositionSeconds is 0) {
            await UpdatePlaybackAsync(
                request.EntityId,
                resumeSeconds: 0,
                request.DurationSeconds,
                request.Completed,
                request.ActivitySeconds,
                request.UtcOffsetMinutes,
                cancellationToken);
        }
    }

    public async Task ProgressAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken) {
        RegisterOrPing(request);
        if (request.EntityId != Guid.Empty && request.PositionSeconds is >= 0) {
            await UpdatePlaybackAsync(
                request.EntityId,
                request.PositionSeconds.Value,
                request.DurationSeconds,
                request.Completed,
                request.ActivitySeconds,
                request.UtcOffsetMinutes,
                cancellationToken);
        }
    }

    public async Task PingAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken) {
        RegisterOrPing(request);
        if (request.EntityId != Guid.Empty && request.ActivitySeconds is > 0 && request.PositionSeconds is >= 0) {
            await UpdatePlaybackAsync(
                request.EntityId,
                request.PositionSeconds.Value,
                request.DurationSeconds,
                request.Completed,
                request.ActivitySeconds,
                request.UtcOffsetMinutes,
                cancellationToken);
        }
    }

    public async Task StopAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken) {
        if (request.EntityId != Guid.Empty) {
            await UpdatePlaybackAsync(
                request.EntityId,
                request.PositionSeconds is >= 0 ? request.PositionSeconds.Value : 0,
                request.DurationSeconds,
                request.Completed,
                request.ActivitySeconds,
                request.UtcOffsetMinutes,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.SessionId)) {
            await _transcodes.CancelAsync(request.SessionId!, cancellationToken);
        }
    }

    private async Task<bool> UpdatePlaybackAsync(
        Guid itemId,
        double resumeSeconds,
        double? durationSeconds,
        bool? completed,
        double? activitySeconds,
        int? utcOffsetMinutes,
        CancellationToken cancellationToken) =>
        await _capabilities.UpdateVideoPlaybackAsync(
            itemId,
            resumeSeconds,
            durationSeconds,
            completed,
            activitySeconds,
            utcOffsetMinutes,
            cancellationToken) is not null;

    private void RegisterOrPing(VideoPlaybackSessionCommand request) {
        if (string.IsNullOrWhiteSpace(request.SessionId)) {
            return;
        }

        if (request.EntityId == Guid.Empty) {
            _transcodes.Ping(request.SessionId!);
            return;
        }

        _transcodes.Register(request.SessionId!, request.EntityId);
        _transcodes.Ping(request.SessionId!);
    }
}
