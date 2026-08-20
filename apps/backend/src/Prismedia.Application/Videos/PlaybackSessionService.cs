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
        await RecordSessionPlaybackAsync(request, cancellationToken);
    }

    public async Task PingAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken) {
        RegisterOrPing(request);
        if (request.ActivitySeconds is > 0) {
            await RecordSessionPlaybackAsync(request, cancellationToken);
        }
    }

    public async Task StopAsync(VideoPlaybackSessionCommand request, CancellationToken cancellationToken) {
        await RecordSessionPlaybackAsync(request, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SessionId)) {
            await _transcodes.CancelAsync(request.SessionId!, cancellationToken);
        }
    }

    /// <summary>
    /// Persists a mid-session playback report. A reported position of exactly zero is not
    /// authoritative here: a player that is still opening or has stalled reports zero even when
    /// the user is resuming partway in, so accepting it would destroy the saved resume point.
    /// Only the explicit start-over signal in <see cref="StartAsync"/> clears a resume position;
    /// zero-position progress and stop reports keep their activity and completion payloads.
    /// </summary>
    private async Task RecordSessionPlaybackAsync(
        VideoPlaybackSessionCommand request,
        CancellationToken cancellationToken) {
        if (request.EntityId == Guid.Empty) {
            return;
        }

        double? resumeSeconds = request.PositionSeconds is > 0 ? request.PositionSeconds : null;
        if (resumeSeconds is null && request.ActivitySeconds is not > 0 && request.Completed is null) {
            return;
        }

        await UpdatePlaybackAsync(
            request.EntityId,
            resumeSeconds,
            request.DurationSeconds,
            request.Completed,
            request.ActivitySeconds,
            request.UtcOffsetMinutes,
            cancellationToken);
    }

    private async Task<bool> UpdatePlaybackAsync(
        Guid itemId,
        double? resumeSeconds,
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
