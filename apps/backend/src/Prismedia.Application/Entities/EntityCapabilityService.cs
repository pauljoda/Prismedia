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
    private readonly IEntityReadService _entityReads;
    private readonly IEntityProgressTopologyResolver _progressTopology;
    private readonly IEntityVisibilityChecker? _visibility;
    private readonly IConsumptionEventStore _consumptionEvents;
    private readonly IConsumptionActivityStore _consumptionActivities;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Maximum active duration accepted from one reader/player heartbeat. This bounds time inflation
    /// after suspended tabs or stale clients resume and report one oversized wall-clock interval.
    /// </summary>
    private const double MaxActivityHeartbeatSeconds = 60;

    /// <summary>Largest real-world wall-clock offset accepted for daily buckets.</summary>
    private const int MaxUtcOffsetMinutes = 16 * 60;

    /// <summary>
    /// Creates the service over the entity write port.
    /// </summary>
    /// <param name="entities">Entity write repository implemented by Infrastructure.</param>
    public EntityCapabilityService(
        IEntityWriteRepository entities,
        IEntityReadService entityReads,
        IEntityProgressTopologyResolver progressTopology,
        IEntityVisibilityChecker? visibility = null,
        IConsumptionEventStore? consumptionEvents = null,
        TimeProvider? timeProvider = null,
        IConsumptionActivityStore? consumptionActivities = null) {
        _entities = entities;
        _entityReads = entityReads;
        _progressTopology = progressTopology;
        _visibility = visibility;
        _consumptionEvents = consumptionEvents ?? NullConsumptionEventStore.Instance;
        _consumptionActivities = consumptionActivities ?? NullConsumptionActivityStore.Instance;
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
        }, new EntityMutableStateChange(userOpinionChanged: true), cancellationToken);

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
        }, new EntityMutableStateChange(
            userOpinionChanged: isFavorite.HasValue,
            curationFlagsChanged: isNsfw.HasValue || isOrganized.HasValue), cancellationToken);

    /// <summary>Fraction of the runtime below which an item is treated as not started.</summary>
    private const double StartedFraction = 0.05;

    /// <summary>Fraction of a video runtime at or above which the item is treated as watched.</summary>
    private const double VideoWatchedFraction = 0.95;

    /// <summary>
    /// Updates timed resume and consumption state using canonical Prismedia thresholds so all
    /// first-party clients converge on identical state for the same inputs. When
    /// <paramref name="completed"/> is <c>null</c> (the normal progress/stop
    /// path) video/movie watched state is derived from <paramref name="resumeSeconds"/>
    /// relative to the entity's known runtime: at or above <see cref="VideoWatchedFraction"/>
    /// the item is completed (and the completion count incremented), below <see cref="StartedFraction"/>
    /// it is treated as a fresh start, and in between the position is stored for resume.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="resumeSeconds">Current playback position in seconds, when known.</param>
    /// <param name="durationSeconds">Active viewing-time delta to accumulate, when reported.</param>
    /// <param name="completed">Explicit completion override; <c>null</c> derives from position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<EntityCard?> UpdatePlaybackAsync(
        Guid id,
        double? resumeSeconds,
        double? durationSeconds,
        bool? completed,
        CancellationToken cancellationToken) =>
        await UpdatePlaybackAsync(id, resumeSeconds, durationSeconds, completed, null, cancellationToken);

    /// <summary>Updates playback position and active time in the client's local daily bucket.</summary>
    public async Task<EntityCard?> UpdatePlaybackAsync(
        Guid id,
        double? resumeSeconds,
        double? durationSeconds,
        bool? completed,
        int? utcOffsetMinutes,
        CancellationToken cancellationToken) =>
        await UpdatePlaybackCoreAsync(
            id,
            resumeSeconds,
            activitySeconds: durationSeconds,
            activityKind: null,
            mediaDurationSeconds: null,
            completed,
            utcOffsetMinutes,
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
        await UpdateVideoPlaybackAsync(
            id,
            positionSeconds,
            mediaDurationSeconds,
            completed,
            activitySeconds: null,
            utcOffsetMinutes: null,
            cancellationToken);

    /// <summary>Updates video position and a bounded active-viewing interval.</summary>
    public async Task<EntityCard?> UpdateVideoPlaybackAsync(
        Guid id,
        double? positionSeconds,
        double? mediaDurationSeconds,
        bool? completed,
        double? activitySeconds,
        int? utcOffsetMinutes,
        CancellationToken cancellationToken) =>
        await UpdatePlaybackCoreAsync(
            id,
            positionSeconds,
            activitySeconds,
            activityKind: ConsumptionActivityKind.Viewing,
            mediaDurationSeconds,
            completed,
            utcOffsetMinutes,
            cancellationToken);

    private async Task<EntityCard?> UpdatePlaybackCoreAsync(
        Guid id,
        double? resumeSeconds,
        double? activitySeconds,
        ConsumptionActivityKind? activityKind,
        double? mediaDurationSeconds,
        bool? completed,
        int? utcOffsetMinutes,
        CancellationToken cancellationToken) {
        // One logical player report retains one timestamp across optimistic-concurrency retries.
        // This keeps completed/history semantics deterministic when a racing heartbeat wins first.
        var occurredAt = _timeProvider.GetUtcNow();
        var card = await MutateWithConsumptionAsync(id, entity => {
            var consumption = GetOrAddDefaultCapability<CapabilityConsumption>(entity);
            if (consumption is null) {
                return ConsumptionMutationResult.Rejected;
            }
            if (entity.Definition.Engagement.Mode == EntityEngagementMode.None &&
                (resumeSeconds is not null || completed is not null)) {
                return ConsumptionMutationResult.Rejected;
            }
            if (BoundActivitySeconds(activitySeconds) is not null &&
                activityKind is null &&
                entity.Definition.Engagement.DefaultActivityKind is null) {
                return ConsumptionMutationResult.Rejected;
            }

            ConsumptionEventAppend? completedEvent = null;
            ConsumptionActivityAppend? activity = null;
            var completionCountBefore = consumption.Value.CompletionCount;

            if (BoundActivitySeconds(activitySeconds) is { } boundedSeconds) {
                consumption.AccumulateActiveDuration(TimeSpan.FromSeconds(boundedSeconds), occurredAt);
                activity = new ConsumptionActivityAppend(
                    entity.Id,
                    activityKind ?? entity.Definition.Engagement.DefaultActivityKind ?? ConsumptionActivityKind.Viewing,
                    ActivityDate(occurredAt, utcOffsetMinutes),
                    boundedSeconds);
            }

            // Position and completion reports are timestamped user observations. A retry may
            // reapply an older heartbeat after a newer one committed, so only the newest signal
            // may change cursor/completion state. Reported duration remains additive above.
            var acceptsSignal = AcceptsConsumptionSignal(consumption, occurredAt);

            // Explicit watched toggle. The completion flag is independent of the resume position:
            // a resume value is only applied when the caller supplies one, so the in-app
            // toggle leaves the position untouched.
            if (completed is { } watched) {
                if (acceptsSignal && resumeSeconds is { } toggleSeconds) {
                    consumption.RecordResume(TimeSpan.FromSeconds(Math.Max(0, toggleSeconds)), occurredAt);
                }

                if (acceptsSignal && watched) {
                    consumption.MarkCompleted(occurredAt);
                    if (consumption.Value.CompletionCount > completionCountBefore) {
                        completedEvent = CompletedEvent(
                            entity,
                            occurredAt,
                            resumeSeconds,
                            mediaDurationSeconds ?? entity.Technical?.Duration?.TotalSeconds);
                    }
                } else if (acceptsSignal) {
                    consumption.MarkIncomplete(occurredAt);
                }

                return ConsumptionMutationResult.Applied(completedEvent, activity);
            }

            if (resumeSeconds is not { } seconds) {
                return ConsumptionMutationResult.Applied(activity: activity);
            }

            var position = TimeSpan.FromSeconds(Math.Max(0, seconds));
            var runtime = mediaDurationSeconds is > 0
                ? TimeSpan.FromSeconds(mediaDurationSeconds.Value)
                : entity.Technical?.Duration;
            if (runtime is not { } total || total <= TimeSpan.Zero) {
                if (acceptsSignal) {
                    consumption.RecordResume(position, occurredAt);
                }
                return ConsumptionMutationResult.Applied(activity: activity);
            }

            var fraction = position.TotalSeconds / total.TotalSeconds;
            if (entity.Definition.Engagement.DerivesCompletionFromPlaybackFraction &&
                fraction >= VideoWatchedFraction && acceptsSignal) {
                consumption.RecordCompleted(occurredAt);
                if (consumption.Value.CompletionCount > completionCountBefore) {
                    completedEvent = CompletedEvent(entity, occurredAt, position.TotalSeconds, total.TotalSeconds);
                }
            } else if (fraction < StartedFraction && acceptsSignal) {
                consumption.RecordStartOver(occurredAt);
            } else if (acceptsSignal) {
                // A later player report may intentionally seek backward; only an older report
                // loses to the already persisted signal above.
                consumption.RecordResume(position, occurredAt);
            }

            return ConsumptionMutationResult.Applied(completedEvent, activity);
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
        var card = await MutateWithConsumptionAsync(id, entity => {
            var consumption = GetOrAddDefaultCapability<CapabilityConsumption>(entity);
            if (consumption is null || entity.Definition.Engagement.Mode == EntityEngagementMode.None) {
                return ConsumptionMutationResult.Rejected;
            }

            consumption.RecordCompletedOccurrence(now);
            return ConsumptionMutationResult.Applied(
                CompletedEvent(entity, now, positionSeconds: null, durationSeconds: entity.Technical?.Duration?.TotalSeconds));
        }, cancellationToken);

        return card;
    }

    /// <summary>
    /// Records an explicit playback-history event and updates the aggregate playback counters.
    /// </summary>
    public async Task<EntityCard?> RecordPlaybackEventAsync(
        Guid id,
        ConsumptionEventKind kind,
        DateTimeOffset? occurredAt,
        double? positionSeconds,
        double? durationSeconds,
        CancellationToken cancellationToken) =>
        await RecordPlaybackEventAsync(id, kind, occurredAt, positionSeconds, durationSeconds, null, cancellationToken);

    /// <summary>Records one explicit consumption event, optionally tied to a playback session.</summary>
    public async Task<EntityCard?> RecordPlaybackEventAsync(
        Guid id,
        ConsumptionEventKind kind,
        DateTimeOffset? occurredAt,
        double? positionSeconds,
        double? durationSeconds,
        string? sessionId,
        CancellationToken cancellationToken) =>
        kind switch {
            ConsumptionEventKind.Accessed => await RecordAccessedAsync(
                id,
                occurredAt ?? _timeProvider.GetUtcNow(),
                positionSeconds,
                durationSeconds,
                sessionId,
                cancellationToken),
            ConsumptionEventKind.Completed => await RecordCompletedPlaybackAsync(
                id,
                occurredAt ?? _timeProvider.GetUtcNow(),
                positionSeconds,
                durationSeconds,
                cancellationToken),
            ConsumptionEventKind.Skipped => await RecordSkippedPlaybackAsync(
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
        var card = await MutateWithConsumptionAsync(id, entity => {
            var consumption = GetOrAddDefaultCapability<CapabilityConsumption>(entity);
            if (consumption is null || entity.Definition.Engagement.Mode == EntityEngagementMode.None) {
                return ConsumptionMutationResult.Rejected;
            }

            consumption.RecordCompletedOccurrence(occurredAt);
            return ConsumptionMutationResult.Applied(
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
        var card = await MutateWithConsumptionAsync(id, entity => {
            var consumption = GetOrAddDefaultCapability<CapabilityConsumption>(entity);
            if (consumption is null || entity.Definition.Engagement.Mode != EntityEngagementMode.Playback) {
                return ConsumptionMutationResult.Rejected;
            }

            consumption.RecordSkipped(occurredAt);
            return ConsumptionMutationResult.Applied(new ConsumptionEventAppend(
                entity.Id,
                ConsumptionEventKind.Skipped,
                occurredAt,
                positionSeconds,
                durationSeconds ?? entity.Technical?.Duration?.TotalSeconds));
        }, cancellationToken);

        return card;
    }

    /// <summary>Records one explicit open/start event.</summary>
    public async Task<EntityCard?> RecordAccessedAsync(
        Guid id,
        DateTimeOffset occurredAt,
        double? positionSeconds,
        double? durationSeconds,
        string? sessionId,
        CancellationToken cancellationToken) {
        var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
        return await MutateWithConsumptionAsync(id, entity => {
            var consumption = GetOrAddDefaultCapability<CapabilityConsumption>(entity);
            if (consumption is null) {
                return ConsumptionMutationResult.Rejected;
            }

            consumption.RecordAccessed(occurredAt);
            if (positionSeconds is { } seconds) {
                consumption.RecordResume(TimeSpan.FromSeconds(Math.Max(0, seconds)), occurredAt);
            }
            return ConsumptionMutationResult.Applied(new ConsumptionEventAppend(
                entity.Id,
                ConsumptionEventKind.Accessed,
                occurredAt,
                positionSeconds,
                durationSeconds ?? entity.Technical?.Duration?.TotalSeconds,
                normalizedSessionId));
        }, cancellationToken, normalizedSessionId);
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
        ConsumptionActivityKind? activityKind,
        CancellationToken cancellationToken) {
        return await UpdateProgressAsync(
            id,
            currentEntityId,
            unit,
            index,
            total,
            mode,
            completed,
            reset,
            location,
            activitySeconds,
            activityKind,
            utcOffsetMinutes: null,
            cancellationToken);
    }

    /// <summary>Updates the last-active cursor and independent consumed-unit coverage.</summary>
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
        ConsumptionActivityKind? activityKind,
        int? utcOffsetMinutes,
        CancellationToken cancellationToken) {
        // A reader heartbeat is one action even if it races another client. Keep its timestamp
        // stable while every retry reloads topology and latest-cursor state from the database.
        var occurredAt = _timeProvider.GetUtcNow();
        return await ExecuteWriteAttemptAsync(
            attemptCancellationToken => UpdateProgressAttemptAsync(
                id,
                currentEntityId,
                unit,
                index,
                total,
                mode,
                completed,
                reset,
                location,
                activitySeconds,
                activityKind,
                utcOffsetMinutes,
                occurredAt,
                attemptCancellationToken),
            cancellationToken);
    }

    private async Task<EntityCard?> UpdateProgressAttemptAsync(
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
        ConsumptionActivityKind? activityKind,
        int? utcOffsetMinutes,
        DateTimeOffset occurredAt,
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
        var hasActivity = await AccumulateConsumptionActivityAsync(
            entity,
            activitySeconds,
            activityKind,
            utcOffsetMinutes,
            occurredAt,
            cancellationToken);

        // Explicit "mark unread": clear completion in place, independent of the cursor. Bypasses the
        // cursor mutation so a finished item can be reopened without losing the page
        // position. It still obeys the latest-signal timestamp so an old client cannot reopen a
        // newer completion.
        if (!reset && completed == false) {
            if (progress.TryMarkIncomplete(occurredAt) || hasActivity) {
                await SaveProgressStateAsync(entity, cancellationToken);
            }
            return await ReadCardAsync(entity, cancellationToken);
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

        var consumedTotal = proposedPosition?.Total ?? normalizedTotal;
        var consumedIndex = proposedPosition?.Index ?? normalizedIndex;
        var consumedCount = reset
            ? 0
            : completed == true
                ? consumedTotal
                : Math.Max(progress.ConsumedCount, consumedTotal > 0 ? consumedIndex + 1 : 0);

        // Explicit start-over resets coverage; ordinary progress always follows the most recent
        // accepted cursor even when it moved backward.
        if (reset) {
            progress.TryMarkIncomplete(occurredAt);
            if (progress.TryMoveTo(
                    targetCursorId,
                    unit,
                    normalizedIndex,
                    normalizedTotal,
                    mode,
                    occurredAt,
                    normalizedLocation,
                    consumedCount: 0) || hasActivity) {
                await SaveProgressStateAsync(entity, cancellationToken);
            }
            return await ReadCardAsync(entity, cancellationToken);
        }

        if (!progress.TryMoveTo(
                targetCursorId,
                unit,
                normalizedIndex,
                normalizedTotal,
                mode,
                occurredAt,
                normalizedLocation,
                completed: completed == true,
                consumedCount: consumedCount)) {
            if (hasActivity) {
                await SaveProgressStateAsync(entity, cancellationToken);
            }
            return await ReadCardAsync(entity, cancellationToken);
        }

        ConsumptionEventAppend? completedEvent = null;
        if (completed == true) {
            var consumption = GetOrAddDefaultCapability<CapabilityConsumption>(entity);
            if (consumption is not null) {
                consumption.RecordCompletedOccurrence(occurredAt);
                completedEvent = CompletedEvent(entity, occurredAt, positionSeconds: null, durationSeconds: null);
            }
        }

        if (completedEvent is not null) {
            await _consumptionEvents.StageAsync(completedEvent, cancellationToken);
        }
        await SaveProgressStateAsync(entity, cancellationToken);

        return await ReadCardAsync(entity, cancellationToken);
    }

    private async Task<bool> AccumulateConsumptionActivityAsync(
        Entity entity,
        double? activitySeconds,
        ConsumptionActivityKind? activityKind,
        int? utcOffsetMinutes,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) {
        if (entity.Definition.Engagement.Mode == EntityEngagementMode.None ||
            BoundActivitySeconds(activitySeconds) is not { } boundedSeconds) {
            return false;
        }

        var consumption = GetOrAddDefaultCapability<CapabilityConsumption>(entity);
        if (consumption is null) {
            return false;
        }

        consumption.AccumulateActiveDuration(TimeSpan.FromSeconds(boundedSeconds), occurredAt);
        await _consumptionActivities.StageAsync(new ConsumptionActivityAppend(
            entity.Id,
            activityKind ?? entity.Definition.Engagement.DefaultActivityKind ?? ConsumptionActivityKind.Reading,
            ActivityDate(occurredAt, utcOffsetMinutes),
            boundedSeconds),
            cancellationToken);
        return true;
    }

    private Task SaveProgressStateAsync(Entity entity, CancellationToken cancellationToken) =>
        _entities.SaveMutableStateAsync(
            entity,
            new EntityMutableStateChange(
                changedCapabilityTypes: [typeof(CapabilityProgress), typeof(CapabilityConsumption)]),
            cancellationToken);

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
        }, new EntityMutableStateChange(changedCapabilityTypes: [typeof(CapabilityMarkers)]), cancellationToken);

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
            new EntityMutableStateChange(changedCapabilityTypes: [typeof(CapabilityMarkers)]),
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
            new EntityMutableStateChange(changedCapabilityTypes: [typeof(CapabilityMarkers)]),
            cancellationToken);

    /// <summary>Maximum optimistic-concurrency retries for a single user-state mutation.</summary>
    private const int MaxConcurrencyRetries = 4;

    /// <summary>
    /// Executes one complete user-state mutation attempt. The callback must load fresh state and
    /// reapply its action; this method owns attempt cleanup so a conflict never leaves stale rows
    /// or staged playback/activity events in the shared request unit of work.
    /// </summary>
    private async Task<TResult> ExecuteWriteAttemptAsync<TResult>(
        Func<CancellationToken, Task<TResult>> executeAttemptAsync,
        CancellationToken cancellationToken) {
        for (var attempt = 0; ; attempt++) {
            using var writeAttempt = _entities.BeginAttempt();
            try {
                return await executeAttemptAsync(cancellationToken);
            } catch (EntityConcurrencyConflictException) {
                await writeAttempt.RollbackAsync(cancellationToken);
                if (attempt >= MaxConcurrencyRetries) {
                    throw;
                }
            }
        }
    }

    private async Task<EntityCard?> MutateAsync(
        Guid id,
        Func<Entity, bool> mutate,
        EntityMutableStateChange change,
        CancellationToken cancellationToken) {
        // Entities hidden from the current user (library access, disabled roots) must be
        // unmutatable and indistinguishable from missing ones.
        if (_visibility is not null && !await _visibility.IsVisibleAsync(id, cancellationToken)) {
            return null;
        }

        // Reload-and-reapply on conflict: rapid playback reports (Infuse fires pause/unpause within
        // milliseconds) race to write the same entity's state, and a lost optimistic-concurrency
        // write must be retried against the latest row rather than surfaced as a 500.
        return await ExecuteWriteAttemptAsync(
            async attemptCancellationToken => {
                var entity = await _entities.FindShallowAsync(id, attemptCancellationToken);
                if (entity is null || !mutate(entity)) {
                    return null;
                }

                await _entities.SaveMutableStateAsync(entity, change, attemptCancellationToken);
                return await ReadCardAsync(entity, attemptCancellationToken);
            },
            cancellationToken);
    }

    private async Task<EntityCard?> MutateWithConsumptionAsync(
        Guid id,
        Func<Entity, ConsumptionMutationResult> mutate,
        CancellationToken cancellationToken,
        string? accessSessionId = null) {
        if (_visibility is not null && !await _visibility.IsVisibleAsync(id, cancellationToken)) {
            return null;
        }

        // Consumption counters and history describe the same user action. Stage the
        // event before saving so EF persists both inside the entity repository transaction.
        return await ExecuteWriteAttemptAsync(
            async attemptCancellationToken => {
                if (accessSessionId is not null &&
                    await _consumptionEvents.ContainsSessionEventAsync(
                        id,
                        accessSessionId,
                        ConsumptionEventKind.Accessed,
                        attemptCancellationToken)) {
                    return await _entityReads.GetAsync(id, hideNsfw: false, attemptCancellationToken);
                }

                var entity = await _entities.FindShallowAsync(id, attemptCancellationToken);
                if (entity is null) {
                    return null;
                }

                var result = mutate(entity);
                if (!result.WasApplied) {
                    return null;
                }

                if (result.Event is not null) {
                    await _consumptionEvents.StageAsync(result.Event, attemptCancellationToken);
                }
                if (result.Activity is not null) {
                    await _consumptionActivities.StageAsync(result.Activity, attemptCancellationToken);
                }

                await _entities.SaveMutableStateAsync(
                    entity,
                    new EntityMutableStateChange(changedCapabilityTypes: [typeof(CapabilityConsumption)]),
                    attemptCancellationToken);
                return await ReadCardAsync(entity, attemptCancellationToken);
            },
            cancellationToken);
    }

    private static ConsumptionEventAppend CompletedEvent(
        Entity entity,
        DateTimeOffset occurredAt,
        double? positionSeconds,
        double? durationSeconds) =>
        new(
            entity.Id,
            ConsumptionEventKind.Completed,
            occurredAt,
            positionSeconds,
            durationSeconds ?? entity.Technical?.Duration?.TotalSeconds);

    private static bool AcceptsConsumptionSignal(
        CapabilityConsumption consumption,
        DateTimeOffset occurredAt) =>
        consumption.Value.LastActiveAt is null || occurredAt >= consumption.Value.LastActiveAt;

    private static double? BoundActivitySeconds(double? reportedSeconds) =>
        reportedSeconds is { } seconds && double.IsFinite(seconds) && seconds > 0
            ? Math.Min(seconds, MaxActivityHeartbeatSeconds)
            : null;

    private static DateOnly ActivityDate(DateTimeOffset occurredAt, int? utcOffsetMinutes) {
        var offset = TimeSpan.FromMinutes(Math.Clamp(
            utcOffsetMinutes ?? 0,
            -MaxUtcOffsetMinutes,
            MaxUtcOffsetMinutes));
        return DateOnly.FromDateTime(occurredAt.ToOffset(offset).Date);
    }

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

    private async Task<EntityCard> ReadCardAsync(
        Entity entity,
        CancellationToken cancellationToken) {
        return await _entityReads.GetAsync(entity.Id, hideNsfw: false, cancellationToken)
            ?? throw new InvalidOperationException($"Mutated entity '{entity.Id}' is no longer readable.");
    }

    private async Task RollUpOrderedProgressAsync(
        Guid playableId,
        EntityCard playableCard,
        CancellationToken cancellationToken) {
        var consumption = playableCard.Capabilities.OfType<ConsumptionCapability>().SingleOrDefault();
        if (consumption is null || consumption.ResumeSeconds <= 0 && consumption.CompletedAt is null) {
            return;
        }

        var scopes = await _progressTopology.ResolveOrderedScopesAsync(playableId, cancellationToken);
        foreach (var scope in scopes) {
            await UpdateOrderedProgressScopeAsync(scope, consumption.CompletedAt is not null, cancellationToken);
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
        var scopeCompleted = scope.Total > 0 && scope.CompletedCount >= scope.Total;
        var occurredAt = _timeProvider.GetUtcNow();

        await ExecuteWriteAttemptAsync(
            async attemptCancellationToken => {
                var owner = await _entities.FindShallowAsync(scope.OwnerId, attemptCancellationToken);
                if (owner is null) {
                    return false;
                }

                var progress = GetOrAddDefaultCapability<CapabilityProgress>(owner);
                if (progress is null) {
                    return false;
                }
                if (!progress.TryMoveTo(
                        targetItemId,
                        ProgressUnit.Item,
                        targetIndex,
                        scope.Total,
                        mode: null,
                        occurredAt,
                        completed: scopeCompleted,
                        consumedCount: scope.CompletedCount)) {
                    return false;
                }

                await _entities.SaveMutableStateAsync(
                    owner,
                    new EntityMutableStateChange(changedCapabilityTypes: [typeof(CapabilityProgress)]),
                    attemptCancellationToken);
                return true;
            },
            cancellationToken);
    }

    private sealed record ConsumptionMutationResult(
        bool WasApplied,
        ConsumptionEventAppend? Event,
        ConsumptionActivityAppend? Activity) {
        public static ConsumptionMutationResult Rejected { get; } = new(false, null, null);

        public static ConsumptionMutationResult Applied(
            ConsumptionEventAppend? consumptionEvent = null,
            ConsumptionActivityAppend? activity = null) =>
            new(true, consumptionEvent, activity);
    }

}
