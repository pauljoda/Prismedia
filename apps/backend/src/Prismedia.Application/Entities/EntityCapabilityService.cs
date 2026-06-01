using Prismedia.Contracts.Entities;
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

    /// <summary>
    /// Creates the service over the entity write port.
    /// </summary>
    /// <param name="entities">Entity write repository implemented by Infrastructure.</param>
    public EntityCapabilityService(IEntityWriteRepository entities) {
        _entities = entities;
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

    /// <summary>Fraction of the runtime at or above which an item is treated as watched.</summary>
    private const double WatchedFraction = 0.90;

    /// <summary>
    /// Updates the entity's playback capability using Jellyfin-compatible thresholds so the
    /// native player and Jellyfin clients (e.g. Infuse) converge on identical state for the
    /// same inputs. When <paramref name="completed"/> is <c>null</c> (the normal progress/stop
    /// path) the watched/resume decision is derived from <paramref name="resumeSeconds"/>
    /// relative to the entity's known runtime: at or above <see cref="WatchedFraction"/> the
    /// item is completed (and the play count incremented), below <see cref="StartedFraction"/>
    /// it is treated as a fresh start, and in between the position is stored for resume.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="resumeSeconds">Current playback position in seconds, when known.</param>
    /// <param name="durationSeconds">Watched duration delta to accumulate, when reported.</param>
    /// <param name="completed">Explicit completion override; <c>null</c> derives from position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<EntityCard?> UpdatePlaybackAsync(
        Guid id,
        double? resumeSeconds,
        double? durationSeconds,
        bool? completed,
        CancellationToken cancellationToken) =>
        MutateAsync(id, entity => {
            var playback = entity.GetOrAddCapability(() => new CapabilityPlayback());
            var now = DateTimeOffset.UtcNow;

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
                } else {
                    playback.MarkUnwatched(now);
                }

                return true;
            }

            if (resumeSeconds is not { } seconds) {
                return true;
            }

            var position = TimeSpan.FromSeconds(Math.Max(0, seconds));
            var runtime = entity.Technical?.Duration;
            if (runtime is not { } total || total <= TimeSpan.Zero) {
                playback.RecordResume(position, now);
                return true;
            }

            var fraction = position.TotalSeconds / total.TotalSeconds;
            if (fraction >= WatchedFraction) {
                playback.RecordCompleted(now);
            } else if (fraction < StartedFraction) {
                playback.RecordStartOver(now);
            } else {
                playback.RecordResume(position, now);
            }

            return true;
        }, cancellationToken);

    /// <summary>
    /// Updates a non-time progress cursor such as the current chapter and page for books.
    /// </summary>
    public async Task<EntityCard?> UpdateProgressAsync(
        Guid id,
        Guid currentEntityId,
        string unit,
        int index,
        int total,
        string? mode,
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
        var now = DateTimeOffset.UtcNow;

        // Explicit "mark unread": clear completion in place, independent of the cursor. Bypasses the
        // forward-only guard so a finished item can be reopened without losing the page position.
        if (!reset && completed == false) {
            progress.MarkIncomplete(now);
            await _entities.SaveAsync(entity, cancellationToken);
            return EntityCardProjector.ToCard(entity);
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
        var normalizedUnit = string.IsNullOrWhiteSpace(unit) ? "item" : unit.Trim();
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? null : mode.Trim();
        var normalizedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();

        // Explicit "start over": jump to the requested (start) position and clear completion,
        // bypassing the forward-only guard. MoveTo resets the completion flag.
        if (reset) {
            progress.MoveTo(targetChapterId, normalizedUnit, normalizedIndex, normalizedTotal, normalizedMode, now, normalizedLocation);
            await _entities.SaveAsync(entity, cancellationToken);
            return EntityCardProjector.ToCard(entity);
        }

        var existingPosition = progress.CurrentEntityId is { } existingCurrentId && entity.Kind == EntityKind.Book
            ? await _entities.ResolveBookProgressPositionAsync(
                ownerId,
                existingCurrentId,
                progress.Index,
                progress.Total,
                cancellationToken)
            : null;

        if (progress.CompletedAt is not null ||
            (proposedPosition is not null &&
             existingPosition is not null &&
             proposedPosition.Index < existingPosition.Index)) {
            return EntityCardProjector.ToCard(entity);
        }

        progress.MoveTo(targetChapterId, normalizedUnit, normalizedIndex, normalizedTotal, normalizedMode, now, normalizedLocation);

        if (completed == true) {
            progress.MarkCompleted(now);
        }

        await _entities.SaveAsync(entity, cancellationToken);
        return EntityCardProjector.ToCard(entity);
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
                return EntityCardProjector.ToCard(entity);
            } catch (EntityConcurrencyConflictException) when (attempt < MaxConcurrencyRetries) {
                // Re-read the current row and apply the mutation again on the next loop iteration.
            }
        }
    }

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
