using Prismedia.Contracts.Entities;
using Prismedia.Application.Playback;
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
    private readonly IEntitySourceOwnershipReader _sourceOwnership;
    private readonly IEntityFileDeletionRecoveryReader? _deletionRecovery;
    private readonly IEntityVisibilityChecker? _visibility;
    private readonly IPlaybackEventStore _playbackEvents;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the service over the entity write port.
    /// </summary>
    /// <param name="entities">Entity write repository implemented by Infrastructure.</param>
    public EntityCapabilityService(
        IEntityWriteRepository entities,
        IEntitySourceOwnershipReader sourceOwnership,
        IEntityVisibilityChecker? visibility = null,
        IPlaybackEventStore? playbackEvents = null,
        TimeProvider? timeProvider = null,
        IEntityFileDeletionRecoveryReader? deletionRecovery = null) {
        _entities = entities;
        _sourceOwnership = sourceOwnership;
        _deletionRecovery = deletionRecovery;
        _visibility = visibility;
        _playbackEvents = playbackEvents ?? NullPlaybackEventStore.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
    /// Updates the entity's playback capability using Jellyfin-compatible thresholds so the
    /// native player and Jellyfin clients (e.g. Infuse) converge on identical state for the
    /// same inputs. When <paramref name="completed"/> is <c>null</c> (the normal progress/stop
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
        CancellationToken cancellationToken) {
        var card = await MutateWithPlaybackEventAsync(id, entity => {
            var playback = entity.GetOrAddCapability(() => new CapabilityPlayback());
            var now = _timeProvider.GetUtcNow();
            PlaybackEventAppend? completedEvent = null;
            var playCountBefore = playback.Value.PlayCount;

            if (durationSeconds is > 0) {
                playback.AccumulatePlayDuration(TimeSpan.FromSeconds(durationSeconds.Value));
            }

            // Explicit watched toggle. The completion flag is independent of the resume position:
            // a resume value is only applied when the caller supplies one (e.g. a Jellyfin
            // mark-played sends 0 to clear it), so the in-app toggle leaves the position untouched.
            if (completed is { } watched) {
                if (resumeSeconds is { } toggleSeconds) {
                    playback.RecordResume(TimeSpan.FromSeconds(Math.Max(0, toggleSeconds)), now);
                }

                if (watched) {
                    playback.MarkWatched(now);
                    if (playback.Value.PlayCount > playCountBefore) {
                        completedEvent = CompletedEvent(entity, now, resumeSeconds, durationSeconds);
                    }
                } else {
                    playback.MarkUnwatched(now);
                }

                return completedEvent;
            }

            if (resumeSeconds is not { } seconds) {
                return null;
            }

            var position = TimeSpan.FromSeconds(Math.Max(0, seconds));
            var runtime = entity.Technical?.Duration;
            if (runtime is not { } total || total <= TimeSpan.Zero) {
                playback.RecordResume(position, now);
                return null;
            }

            var fraction = position.TotalSeconds / total.TotalSeconds;
            if (CanDeriveVideoCompletion(entity) && fraction >= VideoWatchedFraction) {
                playback.RecordCompleted(now);
                if (playback.Value.PlayCount > playCountBefore) {
                    completedEvent = CompletedEvent(entity, now, position.TotalSeconds, runtime?.TotalSeconds);
                }
            } else if (fraction < StartedFraction) {
                playback.RecordStartOver(now);
            } else {
                playback.RecordResume(position, now);
            }

            return completedEvent;
        }, cancellationToken);

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
            var playback = entity.GetOrAddCapability(() => new CapabilityPlayback());
            playback.RecordCompletedPlay(now);
            return CompletedEvent(entity, now, positionSeconds: null, durationSeconds: entity.Technical?.Duration?.TotalSeconds);
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
            var playback = entity.GetOrAddCapability(() => new CapabilityPlayback());
            playback.RecordCompletedPlay(occurredAt);
            return CompletedEvent(entity, occurredAt, positionSeconds, durationSeconds ?? entity.Technical?.Duration?.TotalSeconds);
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
            var playback = entity.GetOrAddCapability(() => new CapabilityPlayback());
            playback.RecordSkipped(occurredAt);
            return new PlaybackEventAppend(
                entity.Id,
                PlaybackEventKind.Skipped,
                occurredAt,
                positionSeconds,
                durationSeconds ?? entity.Technical?.Duration?.TotalSeconds);
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
        CancellationToken cancellationToken) {
        var ownerId = await ResolveProgressOwnerIdAsync(id, currentEntityId, cancellationToken);
        var entity = await _entities.FindShallowAsync(ownerId, cancellationToken);
        if (entity is null) {
            return null;
        }

        var progress = entity.GetOrAddCapability(() => new CapabilityProgress());
        var now = _timeProvider.GetUtcNow();

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
        var proposedPosition = entity.Kind == EntityKind.Book
            ? await _entities.ResolveBookProgressPositionAsync(
                ownerId,
                currentEntityId,
                normalizedIndex,
                normalizedTotal,
                cancellationToken)
            : null;

        var targetChapterId = proposedPosition?.ChapterId ?? currentEntityId;
        var normalizedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();

        // Explicit "start over": jump to the requested (start) position and clear completion,
        // bypassing the forward-only guard. MoveTo resets the completion flag.
        if (reset) {
            progress.MoveTo(targetChapterId, unit, normalizedIndex, normalizedTotal, mode, now, normalizedLocation);
            await _entities.SaveAsync(entity, cancellationToken);
            return await ProjectCardAsync(entity, cancellationToken);
        }

        var existingPosition = progress.CurrentEntityId is { } existingCurrentId && entity.Kind == EntityKind.Book
            ? await _entities.ResolveBookProgressPositionAsync(
                ownerId,
                existingCurrentId,
                progress.Index,
                progress.Total,
                cancellationToken)
            : null;

        if (proposedPosition is not null &&
            existingPosition is not null &&
            proposedPosition.Index < existingPosition.Index) {
            return await ProjectCardAsync(entity, cancellationToken);
        }

        if (progress.CompletedAt is not null &&
            (proposedPosition is null ||
             existingPosition is null ||
             proposedPosition.Index <= existingPosition.Index)) {
            return await ProjectCardAsync(entity, cancellationToken);
        }

        progress.MoveTo(targetChapterId, unit, normalizedIndex, normalizedTotal, mode, now, normalizedLocation);

        PlaybackEventAppend? completedEvent = null;
        if (completed == true) {
            var playback = entity.GetOrAddCapability(() => new CapabilityPlayback());
            playback.RecordCompletedPlay(now);
            progress.MarkCompleted(now);
            completedEvent = CompletedEvent(entity, now, positionSeconds: null, durationSeconds: null);
        }

        if (completedEvent is not null) {
            await _playbackEvents.StageAsync(completedEvent, cancellationToken);
        }
        await _entities.SaveAsync(entity, cancellationToken);

        return await ProjectCardAsync(entity, cancellationToken);
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
            entity.GetOrAddCapability(() => new CapabilityMarkers()).Add(title, seconds, endSeconds);
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
        MutateAsync(id, entity =>
            entity.GetOrAddCapability(() => new CapabilityMarkers())
                .Update(markerId, title, seconds, endSeconds),
            cancellationToken);

    /// <summary>
    /// Removes one marker from the entity. Returns the entity card only when the marker existed.
    /// </summary>
    public Task<EntityCard?> DeleteMarkerAsync(
        Guid id,
        Guid markerId,
        CancellationToken cancellationToken) =>
        MutateAsync(id, entity =>
            entity.GetOrAddCapability(() => new CapabilityMarkers()).Delete(markerId),
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
        Func<Entity, PlaybackEventAppend?> mutate,
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

            var playbackEvent = mutate(entity);

            try {
                if (playbackEvent is not null) {
                    await _playbackEvents.StageAsync(playbackEvent, cancellationToken);
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
                recoverableDeletionIds.Contains(entity.Id)));
    }

    private static bool CanDeriveVideoCompletion(Entity entity) =>
        entity.Kind is EntityKind.Video or EntityKind.Movie;

    private async Task<Guid> ResolveProgressOwnerIdAsync(
        Guid requestedId,
        Guid currentEntityId,
        CancellationToken cancellationToken) {
        var requestedOwner = await FindBookAncestorIdAsync(requestedId, cancellationToken);
        if (requestedOwner is { } ownerId) {
            return ownerId;
        }

        if (currentEntityId != requestedId) {
            var currentOwner = await FindBookAncestorIdAsync(currentEntityId, cancellationToken);
            if (currentOwner is { } currentOwnerId) {
                return currentOwnerId;
            }
        }

        return requestedId;
    }

    private async Task<Guid?> FindBookAncestorIdAsync(Guid startId, CancellationToken cancellationToken) {
        var visited = new HashSet<Guid>();
        var currentId = startId;

        while (visited.Add(currentId)) {
            var entity = await _entities.FindShallowAsync(currentId, cancellationToken);
            if (entity is null) {
                return null;
            }

            if (entity.Kind == EntityKind.Book) {
                return entity.Id;
            }

            var parentId = entity.ParentEntityId
                ?? await _entities.FindParentIdAsync(entity.Id, cancellationToken);
            if (parentId is not { } resolvedParentId) {
                return null;
            }

            currentId = resolvedParentId;
        }

        return null;
    }
}
