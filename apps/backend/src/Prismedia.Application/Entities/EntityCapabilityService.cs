using Prismedia.Contracts.Entities;
using Prismedia.Application.Playback;
using Prismedia.Application.Security;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// Application use-case service for mutating an entity's user-state capabilities
/// (rating, flags, playback position, markers). Encapsulates the load → mutate → save
/// orchestration so endpoints stay thin and the domain methods remain the single source
/// of behavioral truth.
///
/// Returns the projected <see cref="EntityCard"/> on success so endpoints can return
/// the response contract directly, or <c>null</c> when no active entity exists for the
/// identifier.
/// </summary>
public sealed class EntityCapabilityService {
    private readonly IEntityWriteRepository _entities;
    private readonly IEntityProgressTopologyResolver _progressTopology;
    private readonly IEntitySourceOwnershipReader _sourceOwnership;
    private readonly IEntityFileDeletionRecoveryReader? _deletionRecovery;
    private readonly IEntityVisibilityChecker? _visibility;
    private readonly IPlaybackEventStore _playbackEvents;
    private readonly IEntityActivityStore _activityEvents;
    private readonly ICurrentUserContext? _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Maximum active duration accepted from one reader/player heartbeat. This bounds time inflation
    /// after suspended tabs or stale clients resume and report one oversized wall-clock interval.
    /// </summary>
    private const double MaxReadingActivityHeartbeatSeconds = 60;

    /// <summary>
    /// Creates the service over the entity write port.
    /// </summary>
    /// <param name="entities">Entity write repository implemented by Infrastructure.</param>
    public EntityCapabilityService(
        IEntityWriteRepository entities,
        IEntitySourceOwnershipReader sourceOwnership,
        IEntityProgressTopologyResolver progressTopology,
        IEntityVisibilityChecker? visibility = null,
        IPlaybackEventStore? playbackEvents = null,
        TimeProvider? timeProvider = null,
        IEntityFileDeletionRecoveryReader? deletionRecovery = null,
        IEntityActivityStore? activityEvents = null,
        ICurrentUserContext? currentUser = null) {
        _entities = entities;
        _progressTopology = progressTopology;
        _sourceOwnership = sourceOwnership;
        _deletionRecovery = deletionRecovery;
        _visibility = visibility;
        _playbackEvents = playbackEvents ?? NullPlaybackEventStore.Instance;
        _activityEvents = activityEvents ?? NullEntityActivityStore.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Sets or clears the entity's user rating.
    /// </summary>
    public Task<EntityCard?> RateAsync(Guid id, int? value, CancellationToken cancellationToken) =>
        MutateAsync(id, entity => {
            if (value is { } v) {
                entity.Rate(v);
            } else {
                entity.ClearRating();
            }

            return true;
        }, cancellationToken);

    /// <summary>
    /// Patches the entity's flags. Any null argument leaves the corresponding flag unchanged.
    /// </summary>
    public Task<EntityCard?> UpdateFlagsAsync(
        Guid id,
        bool? isFavorite,
        bool? isNsfw,
        bool? isOrganized,
        CancellationToken cancellationToken) =>
        MutateAsync(id, entity => {
            entity.PatchFlags(isFavorite, isNsfw, isOrganized);
            return true;
        }, cancellationToken);

    /// <summary>Fraction of the runtime below which an item is treated as not started.</summary>
    private const double StartedFraction = 0.05;

    /// <summary>Fraction of a video runtime at or above which the item is treated as watched.</summary>
    private const double VideoWatchedFraction = 0.95;

    /// <summary>
    /// Updates the entity's playback capability using canonical Prismedia thresholds so all
    /// first-party clients converge on identical state for the same inputs. When
    /// <paramref name="completed"/> is <c>null</c> (the normal progress/stop
    /// path) video/movie watched state is derived from <paramref name="resumeSeconds"/>
    /// relative to the entity's known runtime: at or above <see cref="VideoWatchedFraction"/>
    /// the item is completed (and the play count incremented), below <see cref="StartedFraction"/>
    /// it is treated as a fresh start, and in between the position is stored for resume.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="resumeSeconds">Current playback position in seconds, when known.</param>
    /// <param name="durationSeconds">Watched duration delta to accumulate, when reported.</param>
    /// <param name="completed">Explicit completion override; <c>null</c> derives from position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<EntityCard?> UpdatePlaybackAsync(
        Guid id,
        double? resumeSeconds,
        double? durationSeconds,
        bool? completed,
        CancellationToken cancellationToken) =>
        await UpdatePlaybackCoreAsync(
            id,
            resumeSeconds,
            playDurationDeltaSeconds: durationSeconds,
            mediaDurationSeconds: null,
            completed,
            cancellationToken);

    /// <summary>
    /// Updates video playback from a native playback session. The reported media duration is used
    /// to derive watched state when probe metadata is unavailable; it is not accumulated as time
    /// watched on every heartbeat.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="positionSeconds">Current playback position in seconds.</param>
    /// <param name="mediaDurationSeconds">Total media runtime in seconds, when known.</param>
    /// <param name="completed">Explicit completion override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<EntityCard?> UpdateVideoPlaybackAsync(
        Guid id,
        double? positionSeconds,
        double? mediaDurationSeconds,
        bool? completed,
        CancellationToken cancellationToken) =>
        await UpdatePlaybackCoreAsync(
            id,
            positionSeconds,
            playDurationDeltaSeconds: null,
            mediaDurationSeconds,
            completed,
            cancellationToken);

    private async Task<EntityCard?> UpdatePlaybackCoreAsync(
        Guid id,
        double? resumeSeconds,
        double? playDurationDeltaSeconds,
        double? mediaDurationSeconds,
        bool? completed,
        CancellationToken cancellationToken) {
        var card = await MutateWithPlaybackEventAsync(id, entity => {
            var playback = GetOrAddDefaultCapability<CapabilityPlayback>(entity);
            if (playback is null) {
                return PlaybackMutationResult.Rejected;
            }

            var now = _timeProvider.GetUtcNow();
            PlaybackEventAppend? completedEvent = null;
            var playCountBefore = playback.Value.PlayCount;

            if (playDurationDeltaSeconds is > 0) {
                playback.AccumulatePlayDuration(TimeSpan.FromSeconds(playDurationDeltaSeconds.Value));
            }

            // Explicit watched toggle. The completion flag is independent of the resume position:
            // a resume value is only applied when the caller supplies one, so the in-app
            // toggle leaves the position untouched.
            if (completed is { } watched) {
                if (resumeSeconds is { } toggleSeconds) {
                    playback.RecordResume(TimeSpan.FromSeconds(Math.Max(0, toggleSeconds)), now);
                }

                if (watched) {
                    playback.MarkWatched(now);
                    if (playback.Value.PlayCount > playCountBefore) {
                        completedEvent = CompletedEvent(
                            entity,
                            now,
                            resumeSeconds,
                            mediaDurationSeconds ?? entity.Technical?.Duration?.TotalSeconds);
                    }
                } else {
                    playback.MarkUnwatched(now);
                }

                return PlaybackMutationResult.Applied(completedEvent);
            }

            if (resumeSeconds is not { } seconds) {
                return PlaybackMutationResult.Applied();
            }

            var position = TimeSpan.FromSeconds(Math.Max(0, seconds));
            var runtime = mediaDurationSeconds is > 0
                ? TimeSpan.FromSeconds(mediaDurationSeconds.Value)
                : entity.Technical?.Duration;
            if (runtime is not { } total || total <= TimeSpan.Zero) {
                playback.RecordResume(position, now);
                return PlaybackMutationResult.Applied();
            }

            var fraction = position.TotalSeconds / total.TotalSeconds;
            if (entity.Definition.Engagement.DerivesCompletionFromPlaybackFraction &&
                fraction >= VideoWatchedFraction) {
                playback.RecordCompleted(now);
                if (playback.Value.PlayCount > playCountBefore) {
                    completedEvent = CompletedEvent(entity, now, position.TotalSeconds, total.TotalSeconds);
                }
            } else if (fraction < StartedFraction) {
                playback.RecordStartOver(now);
            } else {
                playback.RecordResume(position, now);
            }

            return PlaybackMutationResult.Applied(completedEvent);
        }, cancellationToken);

        if (card is not null) {
            await RollUpOrderedProgressAsync(id, card, cancellationToken);
        }

        return card;
    }
    /// <summary>
    /// Records a completed playback event from players that report a single end-of-stream signal
    /// instead of continuous position progress.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<EntityCard?> RecordCompletedPlaybackAsync(Guid id, CancellationToken cancellationToken) {
        var now = _timeProvider.GetUtcNow();
        var card = await MutateWithPlaybackEventAsync(id, entity => {
            var playback = GetOrAddDefaultCapability<CapabilityPlayback>(entity);
            if (playback is null) {
                return PlaybackMutationResult.Rejected;
            }

            playback.RecordCompletedPlay(now);
            return PlaybackMutationResult.Applied(
                CompletedEvent(entity, now, positionSeconds: null, durationSeconds: entity.Technical?.Duration?.TotalSeconds));
        }, cancellationToken);

        return card;
    }

    /// <summary>
    /// Records an explicit playback-history event and updates the aggregate playback counters.
    /// </summary>
    public async Task<EntityCard?> RecordPlaybackEventAsync(
        Guid id,
        PlaybackEventKind kind,
        DateTimeOffset? occurredAt,
        double? positionSeconds,
        double? durationSeconds,
        CancellationToken cancellationToken) =>
        kind switch {
            PlaybackEventKind.Completed => await RecordCompletedPlaybackAsync(
                id,
                occurredAt ?? _timeProvider.GetUtcNow(),
                positionSeconds,
                durationSeconds,
                cancellationToken),
            PlaybackEventKind.Skipped => await RecordSkippedPlaybackAsync(
                id,
                occurredAt ?? _timeProvider.GetUtcNow(),
                positionSeconds,
                durationSeconds,
                cancellationToken),
            _ => null
        };

    /// <summary>
    /// Records a completed playback event at a caller-supplied timestamp.
    /// </summary>
    public async Task<EntityCard?> RecordCompletedPlaybackAsync(
        Guid id,
        DateTimeOffset occurredAt,
        double? positionSeconds,
        double? durationSeconds,
        CancellationToken cancellationToken) {
        var card = await MutateWithPlaybackEventAsync(id, entity => {
            var playback = GetOrAddDefaultCapability<CapabilityPlayback>(entity);
            if (playback is null) {
                return PlaybackMutationResult.Rejected;
            }

            playback.RecordCompletedPlay(occurredAt);
            return PlaybackMutationResult.Applied(
                CompletedEvent(entity, occurredAt, positionSeconds, durationSeconds ?? entity.Technical?.Duration?.TotalSeconds));
        }, cancellationToken);

        return card;
    }

    /// <summary>
    /// Records a likely skip/quick-abandon event.
    /// </summary>
    public async Task<EntityCard?> RecordSkippedPlaybackAsync(
        Guid id,
        DateTimeOffset occurredAt,
        double? positionSeconds,
        double? durationSeconds,
        CancellationToken cancellationToken) {
        var card = await MutateWithPlaybackEventAsync(id, entity => {
            var playback = GetOrAddDefaultCapability<CapabilityPlayback>(entity);
            if (playback is null) {
                return PlaybackMutationResult.Rejected;
            }

            playback.RecordSkipped(occurredAt);
            return PlaybackMutationResult.Applied(new PlaybackEventAppend(
                entity.Id,
                PlaybackEventKind.Skipped,
                occurredAt,
                positionSeconds,
                durationSeconds ?? entity.Technical?.Duration?.TotalSeconds));
        }, cancellationToken);

        return card;
    }

    /// <summary>
    /// Updates a non-time progress cursor such as the current chapter and page for books.
    /// </summary>
    public async Task<EntityCard?> UpdateProgressAsync(
        Guid id,
        Guid currentEntityId,
        ProgressUnit unit,
        int index,
        int total,
        ReaderMode? mode,
        bool? completed,
        bool reset,
        string? location,
        double? activitySeconds,
        BookActivityKind? activityKind,
        CancellationToken cancellationToken) {
        // Progress ownership is derived from the requested entity only. A cursor is data within
        // that owner tree; it must never be allowed to redirect this mutation to another work.
        if (_visibility is not null &&
            (!await _visibility.IsVisibleAsync(id, cancellationToken) ||
             !await _visibility.IsVisibleAsync(currentEntityId, cancellationToken))) {
            return null;
        }

        var owner = await _progressTopology.ResolveOwnerAsync(id, cancellationToken);
        if (owner is null) {
            return null;
        }
        if (_visibility is not null && !await _visibility.IsVisibleAsync(owner.OwnerId, cancellationToken)) {
            return null;
        }

        var requested = await _entities.FindShallowAsync(id, cancellationToken);
        if (requested is null) {
            return null;
        }

        var proposedCursor = await _progressTopology.ResolveCursorAsync(
            owner.OwnerId,
            currentEntityId,
            cancellationToken);
        if (proposedCursor is null) {
            return null;
        }

        var entity = owner.OwnerId == requested.Id
            ? requested
            : await _entities.FindShallowAsync(owner.OwnerId, cancellationToken);
        if (entity is null) {
            return null;
        }

        if (!entity.Definition.SupportsDefaultCapability<CapabilityProgress>()) {
            return null;
        }

        var progress = GetOrAddDefaultCapability<CapabilityProgress>(entity)!;
        ProgressCursorResolution? existingCursor = null;
        if (progress.CurrentEntityId is { } existingCurrentId) {
            var existingCursorVisible = _visibility is null ||
                await _visibility.IsVisibleAsync(existingCurrentId, cancellationToken);
            if (existingCursorVisible) {
                existingCursor = await _progressTopology.ResolveCursorAsync(
                    owner.OwnerId,
                    existingCurrentId,
                    cancellationToken);
            }
            // Invalid legacy or newly inaccessible stored cursors are treated as absent so a
            // valid mutation can repair the user's progress instead of becoming permanently stuck.
        }
        var now = _timeProvider.GetUtcNow();
        var hasActivity = await AccumulateReadingActivityAsync(
            entity,
            activitySeconds,
            activityKind,
            now,
            cancellationToken);

        // Explicit "mark unread": clear completion in place, independent of the cursor. Bypasses the
        // forward-only guard so a finished item can be reopened without losing the page position.
        if (!reset && completed == false) {
            progress.MarkIncomplete(now);
            await _entities.SaveAsync(entity, cancellationToken);
            return await ProjectCardAsync(entity, cancellationToken);
        }

        var normalizedTotal = Math.Max(0, total);
        var normalizedIndex = normalizedTotal == 0
            ? 0
            : Math.Clamp(index, 0, normalizedTotal - 1);
        var proposedPosition = await _progressTopology.ResolveWorkPositionAsync(
            owner.OwnerId,
            currentEntityId,
            normalizedIndex,
            normalizedTotal,
            cancellationToken);

        var targetCursorId = proposedPosition?.CursorId ?? proposedCursor.NormalizedCursorId;
        var normalizedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();

        // Explicit "start over": jump to the requested (start) position and clear completion,
        // bypassing the forward-only guard. MoveTo resets the completion flag.
        if (reset) {
            progress.MoveTo(targetCursorId, unit, normalizedIndex, normalizedTotal, mode, now, normalizedLocation);
            await _entities.SaveAsync(entity, cancellationToken);
            return await ProjectCardAsync(entity, cancellationToken);
        }

        var existingPosition = existingCursor is null
            ? null
            : await _progressTopology.ResolveWorkPositionAsync(
                owner.OwnerId,
                existingCursor.CursorId,
                progress.Index,
                progress.Total,
                cancellationToken);

        if (proposedPosition is not null &&
            existingPosition is not null &&
            proposedPosition.Index < existingPosition.Index) {
            if (hasActivity) {
                await _entities.SaveAsync(entity, cancellationToken);
            }
            return await ProjectCardAsync(entity, cancellationToken);
        }

        if (proposedPosition is null &&
            existingPosition is null &&
            IsEarlierComparableCursor(progress, targetCursorId, unit, normalizedIndex, normalizedTotal)) {
            if (hasActivity) {
                await _entities.SaveAsync(entity, cancellationToken);
            }
            return await ProjectCardAsync(entity, cancellationToken);
        }

        // A readable cursor is the safe source of truth when an unmatched audiobook part cannot be
        // mapped into that chapter. A later readable heartbeat may always replace an audio-only one.
        if (unit == ProgressUnit.Second && progress.Unit is ProgressUnit.Page or ProgressUnit.Cfi) {
            if (hasActivity) {
                await _entities.SaveAsync(entity, cancellationToken);
            }
            return await ProjectCardAsync(entity, cancellationToken);
        }

        if (progress.CompletedAt is not null &&
            (proposedPosition is null ||
             existingPosition is null ||
             proposedPosition.Index <= existingPosition.Index)) {
            if (hasActivity) {
                await _entities.SaveAsync(entity, cancellationToken);
            }
            return await ProjectCardAsync(entity, cancellationToken);
        }

        progress.MoveTo(targetCursorId, unit, normalizedIndex, normalizedTotal, mode, now, normalizedLocation);

        PlaybackEventAppend? completedEvent = null;
        if (completed == true) {
            progress.MarkCompleted(now);
            var playback = GetOrAddDefaultCapability<CapabilityPlayback>(entity);
            if (playback is not null) {
                playback.RecordCompletedPlay(now);
                completedEvent = CompletedEvent(entity, now, positionSeconds: null, durationSeconds: null);
            }
        }

        if (completedEvent is not null) {
            await _playbackEvents.StageAsync(completedEvent, cancellationToken);
        }
        await _entities.SaveAsync(entity, cancellationToken);

        return await ProjectCardAsync(entity, cancellationToken);
    }

    private async Task<bool> AccumulateReadingActivityAsync(
        Entity entity,
        double? activitySeconds,
        BookActivityKind? activityKind,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) {
        if (entity.Definition.Engagement.Mode != EntityEngagementMode.Reading ||
            activityKind is null ||
            activitySeconds is not { } reportedSeconds ||
            !double.IsFinite(reportedSeconds) ||
            reportedSeconds <= 0) {
            return false;
        }

        var boundedSeconds = Math.Min(reportedSeconds, MaxReadingActivityHeartbeatSeconds);
        var playback = GetOrAddDefaultCapability<CapabilityPlayback>(entity);
        if (playback is null) {
            return false;
        }

        playback.AccumulatePlayDuration(TimeSpan.FromSeconds(boundedSeconds));
        await _activityEvents.StageAsync(
            new EntityActivityAppend(entity.Id, activityKind.Value, occurredAt, boundedSeconds),
            cancellationToken);
        return true;
    }

    private static bool IsEarlierComparableCursor(
        CapabilityProgress current,
        Guid proposedEntityId,
        ProgressUnit proposedUnit,
        int proposedIndex,
        int proposedTotal) {
        if (current.CurrentEntityId != proposedEntityId ||
            current.Unit != proposedUnit ||
            current.Total <= 0 ||
            proposedTotal <= 0) {
            return false;
        }

        var currentFraction = current.Index / (double)current.Total;
        var proposedFraction = proposedIndex / (double)proposedTotal;
        return proposedFraction < currentFraction;
    }

    /// <summary>
    /// Appends a new marker to the entity's marker capability.
    /// </summary>
    public Task<EntityCard?> AddMarkerAsync(
        Guid id,
        string title,
        double seconds,
        double? endSeconds,
        CancellationToken cancellationToken) =>
        MutateAsync(id, entity => {
            var markers = GetOrAddDefaultCapability<CapabilityMarkers>(entity);
            if (markers is null) {
                return false;
            }

            markers.Add(title, seconds, endSeconds);
            return true;
        }, cancellationToken);

    /// <summary>
    /// Updates one existing marker on the entity. Returns the entity card only when the marker exists.
    /// </summary>
    public Task<EntityCard?> UpdateMarkerAsync(
        Guid id,
        Guid markerId,
        string title,
        double seconds,
        double? endSeconds,
        CancellationToken cancellationToken) =>
        MutateAsync(id, entity => MutateExistingDefaultCapability<CapabilityMarkers>(
            entity,
            markers => markers.Update(markerId, title, seconds, endSeconds)),
            cancellationToken);

    /// <summary>
    /// Removes one marker from the entity. Returns the entity card only when the marker existed.
    /// </summary>
    public Task<EntityCard?> DeleteMarkerAsync(
        Guid id,
        Guid markerId,
        CancellationToken cancellationToken) =>
        MutateAsync(id, entity => MutateExistingDefaultCapability<CapabilityMarkers>(
            entity,
            markers => markers.Delete(markerId)),
            cancellationToken);

    /// <summary>Maximum optimistic-concurrency retries for a single user-state mutation.</summary>
    private const int MaxConcurrencyRetries = 4;

    private async Task<EntityCard?> MutateAsync(
        Guid id,
        Func<Entity, bool> mutate,
        CancellationToken cancellationToken) {
        // Entities hidden from the current user (library access, disabled roots) must be
        // unmutatable and indistinguishable from missing ones.
        if (_visibility is not null && !await _visibility.IsVisibleAsync(id, cancellationToken)) {
            return null;
        }

        // Reload-and-reapply on conflict: rapid playback reports (Infuse fires pause/unpause within
        // milliseconds) race to write the same entity's state, and a lost optimistic-concurrency
        // write must be retried against the latest row rather than surfaced as a 500.
        for (var attempt = 0; ; attempt++) {
            var entity = await _entities.FindShallowAsync(id, cancellationToken);
            if (entity is null || !mutate(entity)) {
                return null;
            }

            try {
                await _entities.SaveAsync(entity, cancellationToken);
                return await ProjectCardAsync(entity, cancellationToken);
            } catch (EntityConcurrencyConflictException) when (attempt < MaxConcurrencyRetries) {
                // Re-read the current row and apply the mutation again on the next loop iteration.
            }
        }
    }

    private async Task<EntityCard?> MutateWithPlaybackEventAsync(
        Guid id,
        Func<Entity, PlaybackMutationResult> mutate,
        CancellationToken cancellationToken) {
        if (_visibility is not null && !await _visibility.IsVisibleAsync(id, cancellationToken)) {
            return null;
        }

        // Playback counters and playback history describe the same user action. Stage the
        // event before saving so EF persists both inside the entity repository transaction.
        for (var attempt = 0; ; attempt++) {
            var entity = await _entities.FindShallowAsync(id, cancellationToken);
            if (entity is null) {
                return null;
            }

            var result = mutate(entity);
            if (!result.WasApplied) {
                return null;
            }

            try {
                if (result.Event is not null) {
                    await _playbackEvents.StageAsync(result.Event, cancellationToken);
                }

                await _entities.SaveAsync(entity, cancellationToken);
                return await ProjectCardAsync(entity, cancellationToken);
            } catch (EntityConcurrencyConflictException) when (attempt < MaxConcurrencyRetries) {
                // Re-read the current row and apply the mutation again on the next loop iteration.
            }
        }
    }

    private static PlaybackEventAppend CompletedEvent(
        Entity entity,
        DateTimeOffset occurredAt,
        double? positionSeconds,
        double? durationSeconds) =>
        new(
            entity.Id,
            PlaybackEventKind.Completed,
            occurredAt,
            positionSeconds,
            durationSeconds ?? entity.Technical?.Duration?.TotalSeconds);

    private static TCapability? GetOrAddDefaultCapability<TCapability>(Entity entity)
        where TCapability : Prismedia.Domain.Capabilities.EntityCapability =>
        entity.Definition.SupportsDefaultCapability<TCapability>()
            ? entity.GetOrAddCapability(entity.Definition.CreateDefaultCapability<TCapability>)
            : null;

    private static bool MutateExistingDefaultCapability<TCapability>(
        Entity entity,
        Func<TCapability, bool> mutate)
        where TCapability : Prismedia.Domain.Capabilities.EntityCapability =>
        entity.Definition.SupportsDefaultCapability<TCapability>() &&
        entity.GetCapability<TCapability>() is { } capability &&
        mutate(capability);

    private async Task<EntityCard> ProjectCardAsync(
        Entity entity,
        CancellationToken cancellationToken) {
        var sourceBackedIds = await _sourceOwnership.ResolveAsync([entity.Id], cancellationToken);
        var recoverableDeletionIds = _deletionRecovery is null
            ? new HashSet<Guid>()
            : await _deletionRecovery.ResolveAsync([entity.Id], cancellationToken);
        return EntityCardProjector.ToCard(
            entity,
            new EntityFileManagementState(
                sourceBackedIds.Contains(entity.Id),
                recoverableDeletionIds.Contains(entity.Id)),
            _currentUser?.UserId);
    }

    private async Task RollUpOrderedProgressAsync(
        Guid playableId,
        EntityCard playableCard,
        CancellationToken cancellationToken) {
        var playback = playableCard.Capabilities.OfType<PlaybackCapability>().SingleOrDefault();
        if (playback is null || playback.ResumeSeconds <= 0 && playback.CompletedAt is null) {
            return;
        }

        var scopes = await _progressTopology.ResolveOrderedScopesAsync(playableId, cancellationToken);
        foreach (var scope in scopes) {
            await UpdateOrderedProgressScopeAsync(scope, playback.CompletedAt is not null, cancellationToken);
        }
    }

    private async Task UpdateOrderedProgressScopeAsync(
        OrderedProgressScope scope,
        bool itemCompleted,
        CancellationToken cancellationToken) {
        var targetItemId = itemCompleted && scope.NextItemId is { } nextItemId
            ? nextItemId
            : scope.CurrentItemId;
        var targetIndex = itemCompleted && scope.NextItemId is not null
            ? scope.Index + 1
            : scope.Index;
        var scopeCompleted = itemCompleted && scope.NextItemId is null;

        for (var attempt = 0; ; attempt++) {
            var owner = await _entities.FindShallowAsync(scope.OwnerId, cancellationToken);
            if (owner is null) {
                return;
            }

            var progress = GetOrAddDefaultCapability<CapabilityProgress>(owner);
            if (progress is null) {
                return;
            }
            if (progress.CurrentEntityId is not null &&
                (progress.Index > targetIndex ||
                 progress.CompletedAt is not null && !scopeCompleted && progress.Index >= targetIndex)) {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            progress.MoveTo(
                targetItemId,
                ProgressUnit.Item,
                targetIndex,
                scope.Total,
                mode: null,
                now);
            if (scopeCompleted) {
                progress.MarkCompleted(now);
            }

            try {
                await _entities.SaveAsync(owner, cancellationToken);
                return;
            } catch (EntityConcurrencyConflictException) when (attempt < MaxConcurrencyRetries) {
                // Re-read the container cursor and preserve the farther position on retry.
            }
        }
    }

    private sealed record PlaybackMutationResult(bool WasApplied, PlaybackEventAppend? Event) {
        public static PlaybackMutationResult Rejected { get; } = new(false, null);

        public static PlaybackMutationResult Applied(PlaybackEventAppend? playbackEvent = null) =>
            new(true, playbackEvent);
    }

}
