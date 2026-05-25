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

    /// <summary>
    /// Updates the entity's playback capability. Seconds inputs are converted to <see cref="TimeSpan"/>.
    /// </summary>
    public Task<EntityCard?> UpdatePlaybackAsync(
        Guid id,
        double? resumeSeconds,
        double? durationSeconds,
        bool? completed,
        CancellationToken cancellationToken) =>
        MutateAsync(id, entity => {
            entity.GetOrAddCapability(() => new CapabilityPlayback()).Update(
                resumeSeconds is null ? null : TimeSpan.FromSeconds(resumeSeconds.Value),
                durationSeconds is null ? null : TimeSpan.FromSeconds(durationSeconds.Value),
                completed,
                DateTimeOffset.UtcNow);
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
        CancellationToken cancellationToken) {
        var ownerId = await ResolveProgressOwnerIdAsync(id, currentEntityId, cancellationToken);
        var entity = await _entities.FindShallowAsync(ownerId, cancellationToken);
        if (entity is null) {
            return null;
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
        var progress = entity.GetOrAddCapability(() => new CapabilityProgress());
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

        var now = DateTimeOffset.UtcNow;
        progress.MoveTo(
            proposedPosition?.ChapterId ?? currentEntityId,
            string.IsNullOrWhiteSpace(unit) ? "item" : unit.Trim(),
            normalizedIndex,
            normalizedTotal,
            string.IsNullOrWhiteSpace(mode) ? null : mode.Trim(),
            now);

        if (completed == true &&
            (proposedPosition is null || proposedPosition.Index >= proposedPosition.Total - 1)) {
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

    private async Task<EntityCard?> MutateAsync(
        Guid id,
        Func<Entity, bool> mutate,
        CancellationToken cancellationToken) {
        var entity = await _entities.FindShallowAsync(id, cancellationToken);
        if (entity is null || !mutate(entity)) {
            return null;
        }

        await _entities.SaveAsync(entity, cancellationToken);
        return EntityCardProjector.ToCard(entity);
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
