using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

/// <summary>
/// Hydrates and persists the reading-progress capability against the current user's
/// <c>user_entity_states</c> row and, for kinds that declare consumption modalities, the
/// <c>user_progress_checkpoints</c> child rows. Without an authenticated user the capability
/// hydrates empty and persists nothing.
/// </summary>
internal sealed class ProgressCapabilityMapper(PrismediaDbContext db, ICurrentUserContext currentUser) :
    IEntityCapabilityMapper,
    IEntityMutableStateMapper<CapabilityProgress> {
    #region Actions - Hydration

    /// <inheritdoc />
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty) {
            return;
        }

        var row = await UserEntityStateColumns.FindAsync(db, userId, entity.Id, cancellationToken);
        if (row is null) {
            return;
        }

        var checkpoints = entity.Definition.Engagement.Modalities.Count > 0
            ? await LoadCheckpointRowsAsync(userId, entity.Id, cancellationToken)
            : [];
        if (!UserEntityStateColumns.HasProgress(row) && checkpoints.Count == 0) {
            return;
        }

        entity.RemoveCapability<CapabilityProgress>();
        // Tolerant decode: rows written before the typed vocabulary may carry legacy values
        // (e.g. mode "paginated" from earlier EPUB saves); those hydrate to the safe defaults
        // instead of failing the whole entity read.
        entity.AddCapability(new CapabilityProgress(
            row.ProgressCurrentEntityId,
            row.ProgressUnit.TryDecodeAs<ProgressUnit>(out var unit) ? unit : ProgressUnit.Item,
            row.ProgressIndex,
            row.ProgressTotal,
            row.ProgressMode is not null && row.ProgressMode.TryDecodeAs<ReaderMode>(out var mode) ? mode : null,
            row.ProgressCompletedAt,
            row.ProgressUpdatedAt ?? row.UpdatedAt,
            row.ProgressLocation,
            row.ProgressConsumedCount,
            DecodeCheckpoints(entity, checkpoints)));
    }

    private Task<List<UserProgressCheckpointRow>> LoadCheckpointRowsAsync(
        Guid userId,
        Guid entityId,
        CancellationToken cancellationToken) =>
        db.UserProgressCheckpoints
            .Where(row => row.UserId == userId && row.EntityId == entityId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Rebuilds each stored row through its modality's validating factory. A row the kind no longer
    /// declares, or one that no longer satisfies its modality's rules, is left out rather than failing
    /// the read; the next accepted signal of that modality replaces it.
    /// </summary>
    private static IReadOnlyList<ProgressCheckpoint> DecodeCheckpoints(
        Entity entity,
        IReadOnlyList<UserProgressCheckpointRow> rows) {
        var checkpoints = new List<ProgressCheckpoint>(rows.Count);
        foreach (var row in rows) {
            if (entity.Definition.Engagement.ModalityFor(row.Modality) is { } definition &&
                DecodeCheckpoint(definition, row) is { } checkpoint) {
                checkpoints.Add(checkpoint);
            }
        }
        return checkpoints;
    }

    /// <summary>
    /// Rebuilds one stored checkpoint row through its modality's validating factory, or returns null
    /// for a legacy or hand-edited row that breaks the modality rules.
    /// </summary>
    internal static ProgressCheckpoint? DecodeCheckpoint(
        ConsumptionModalityDefinition definition,
        UserProgressCheckpointRow row) {
        try {
            return definition.Checkpoint(
                row.PositionEntityId,
                row.Unit,
                row.Index,
                row.Total,
                row.UpdatedAt,
                row.OffsetSeconds,
                row.MarkerId,
                row.Mode,
                row.Location);
        } catch (ArgumentException) {
            return null;
        }
    }

    #endregion

    #region Actions - Persistence

    /// <inheritdoc />
    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty ||
            entity.Progress is not { } progress ||
            progress.UpdatedAt is null && progress.CurrentEntityId is null && progress.Index == 0 && progress.Total == 0 && progress.Location is null && progress.Checkpoints.Count == 0) {
            return;
        }

        var row = await UserEntityStateColumns.GetOrAddAsync(db, userId, entity.Id, cancellationToken);
        row.ProgressCurrentEntityId = progress.CurrentEntityId;
        row.ProgressUnit = progress.Unit.ToCode();
        row.ProgressIndex = progress.Index;
        row.ProgressTotal = progress.Total;
        row.ProgressMode = progress.Mode?.ToCode();
        row.ProgressLocation = progress.Location;
        row.ProgressCompletedAt = progress.CompletedAt;
        var now = DateTimeOffset.UtcNow;
        // A capability that only carries checkpoints has never moved its main cursor; leave the
        // cursor timestamp empty so shelves and latest-signal guards keep treating it as unset.
        row.ProgressUpdatedAt = progress.UpdatedAt ?? row.ProgressUpdatedAt ?? (progress.CurrentEntityId is null ? null : now);
        row.ProgressConsumedCount = progress.ConsumedCount;
        // Touching the parent row on every checkpoint write lets its xmin token serialize
        // concurrent checkpoint writers through the same optimistic-concurrency retry.
        row.UpdatedAt = now;
        if (entity.Definition.Engagement.Modalities.Count > 0) {
            await ReplaceCheckpointRowsAsync(userId, entity.Id, progress, cancellationToken);
        }
    }

    private async Task ReplaceCheckpointRowsAsync(
        Guid userId,
        Guid entityId,
        CapabilityProgress progress,
        CancellationToken cancellationToken) {
        var existing = await LoadCheckpointRowsAsync(userId, entityId, cancellationToken);
        foreach (var stale in existing.Where(row => !progress.Checkpoints.ContainsKey(row.Modality))) {
            db.UserProgressCheckpoints.Remove(stale);
        }

        foreach (var checkpoint in progress.Checkpoints.Values) {
            var row = existing.FirstOrDefault(candidate => candidate.Modality == checkpoint.Modality);
            if (row is null) {
                row = new UserProgressCheckpointRow {
                    UserId = userId,
                    EntityId = entityId,
                    Modality = checkpoint.Modality
                };
                db.UserProgressCheckpoints.Add(row);
            }

            row.PositionEntityId = checkpoint.PositionEntityId;
            row.Unit = checkpoint.Unit;
            row.Index = checkpoint.Index;
            row.Total = checkpoint.Total;
            row.OffsetSeconds = checkpoint.OffsetSeconds;
            row.MarkerId = checkpoint.MarkerId;
            row.Mode = checkpoint.Mode;
            row.Location = checkpoint.Location;
            row.UpdatedAt = checkpoint.UpdatedAt;
        }
    }

    #endregion
}
